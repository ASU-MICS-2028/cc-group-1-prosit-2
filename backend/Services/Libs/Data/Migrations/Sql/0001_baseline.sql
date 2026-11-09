-- Migration 1: the baseline. This is db/schema.sql followed by db/rbac.sql (Lab 6, Liza Aikins), unchanged except
-- that the BEGIN/COMMIT lines are removed (each migration already runs in one transaction).
-- Every statement is written to be safe to run again, so this also works on a database where those two files were
-- already applied by hand. Never edit this file: change the database with a new migration instead.

-- ===================================================== schema.sql =====================================================
-- HealthConnect Ghana: FHIR-aligned patient data model (Lab 6, Part C)
-- Team Godabeg | ICS 534 | PostgreSQL 17 (healthconnect-db, af-south-1)
-- Base design: Liza Aikins (patients / observations / access_log, append-only trigger).
-- Extended for the lab report: coded terminology, identifier namespaces, consent history,
-- a tamper-evident audit chain and database-enforced roles (rbac.sql).
-- Synthetic data only. Shaped like FHIR R4 resources; this is not a FHIR server.


CREATE EXTENSION IF NOT EXISTS pgcrypto;  -- gen_random_uuid(), digest() for the audit chain

-- ---------------------------------------------------------------------------
-- patients  ->  FHIR Patient
-- Every column must be justified under Act 843 s.19 (minimality); see the report.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS patients (
  id                  SERIAL PRIMARY KEY,                          -- internal key (lab starter uses it)
  fhir_id             UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
                                                                   -- Patient.id: phones create records offline,
                                                                   -- so the shareable id cannot be a serial
  fhir_resource_type  TEXT NOT NULL DEFAULT 'Patient'
                      CHECK (fhir_resource_type = 'Patient'),      -- Patient.resourceType
  full_name           TEXT NOT NULL CHECK (length(trim(full_name)) > 0),
                                                                   -- Patient.name.text
  phone               TEXT CHECK (phone ~ '^\+233[0-9]{9}$'),      -- Patient.telecom (optional, E.164 Ghana)
  date_of_birth       DATE CHECK (date_of_birth <= current_date),  -- Patient.birthDate
  birth_date_estimated BOOLEAN NOT NULL DEFAULT false,             -- many rural patients only know their age
  gender              TEXT CHECK (gender IN ('male', 'female', 'other', 'unknown')),
                                                                   -- Patient.gender (FHIR AdministrativeGender)
  region              TEXT,                                        -- Patient.address.state
  district            TEXT NOT NULL
                      DEFAULT coalesce(nullif(current_setting('app.district', true), ''), 'unassigned'),
                                                                   -- Patient.address.district: drives who may see
                                                                   -- the record (rbac.sql) and district counts.
                                                                   -- Defaults to the CHV's own district (set per
                                                                   -- request by the service), so the lab's starter
                                                                   -- POST without a district still works
  preferred_language  TEXT CHECK (preferred_language IN ('en', 'tw', 'ee', 'dag', 'ha')),
                                                                   -- Patient.communication.language (BCP-47)
  consent_given_at    TIMESTAMPTZ,                                 -- NULL until the patient (or CHV on their behalf) consents
  consent_method      TEXT
                      CHECK (consent_method IN ('spoken', 'thumbprint', 'signature')),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  -- consent is recorded completely or not at all: a time without a method (or the reverse) is rejected
  CONSTRAINT consent_recorded_together
    CHECK ((consent_given_at IS NULL) = (consent_method IS NULL))
);
-- nhis_number from the first draft was removed: none of our three purposes (triage,
-- scheduling, advice) uses it yet (Act 843 s.19). If NHIS verification is built, it goes in
-- patient_identifiers with its own namespace, not as a column.

CREATE INDEX IF NOT EXISTS patients_district_idx ON patients (district);

-- ---------------------------------------------------------------------------
-- patient_identifiers  ->  FHIR Patient.identifier (system + value)
-- The gap in the first draft: an identifier without a namespace cannot be matched by
-- another system. Only our own record number is issued today.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS patient_identifiers (
  patient_id  INTEGER NOT NULL REFERENCES patients(id),
  system      TEXT NOT NULL,       -- Identifier.system, e.g. 'urn:healthconnect-gh:mrn'
  value       TEXT NOT NULL,       -- Identifier.value
  use         TEXT NOT NULL DEFAULT 'official' CHECK (use IN ('usual', 'official', 'secondary')),
  PRIMARY KEY (system, value)      -- one identifier, one patient, per namespace
);

-- ---------------------------------------------------------------------------
-- consents  ->  FHIR Consent (history)
-- patients.consent_* holds the current state the lab asks for; this keeps every decision,
-- including withdrawal, which Act 843 s.20(2)-(3) lets a patient do at any time.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS consents (
  id           SERIAL PRIMARY KEY,                                  -- Consent.id
  patient_id   INTEGER NOT NULL REFERENCES patients(id),             -- Consent.patient
  status       TEXT NOT NULL CHECK (status IN ('active', 'inactive', 'rejected')),
                                                                     -- Consent.status
  method       TEXT NOT NULL CHECK (method IN ('spoken', 'thumbprint', 'signature')),
  obtained_by  TEXT NOT NULL,                                         -- Consent.performer (the CHV)
  policy       TEXT NOT NULL DEFAULT 'Act 843 s.20(1), s.37(2)(b)',   -- Consent.policyRule
  purpose      TEXT NOT NULL DEFAULT 'triage, appointment scheduling and health advice',
                                                                     -- told to the patient (s.22, s.23)
  recorded_at  TIMESTAMPTZ NOT NULL DEFAULT now()                    -- Consent.dateTime
);

-- ---------------------------------------------------------------------------
-- observation_codes: the coded terminology the first draft lacked. Every observation must
-- use one of these LOINC codes, with its UCUM unit and a plausibility range, so free text
-- like 'bp' or a temperature in Fahrenheit cannot get in.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS observation_codes (
  loinc_code  TEXT PRIMARY KEY,        -- Observation.code.coding.code (system http://loinc.org)
  display     TEXT NOT NULL,           -- Observation.code.coding.display
  short_name  TEXT NOT NULL UNIQUE,    -- the lab's observation_type
  ucum_unit   TEXT NOT NULL,           -- Observation.valueQuantity.code (system http://unitsofmeasure.org)
  min_value   NUMERIC NOT NULL,
  max_value   NUMERIC NOT NULL
);

INSERT INTO observation_codes VALUES
  ('8480-6',  'Systolic blood pressure',  'systolic_bp',      'mm[Hg]', 50,  260),
  ('8462-4',  'Diastolic blood pressure', 'diastolic_bp',     'mm[Hg]', 30,  160),
  ('8310-5',  'Body temperature',         'body_temperature', 'Cel',    30,  45),
  ('8867-4',  'Heart rate',               'heart_rate',       '/min',   20,  250),
  ('29463-7', 'Body weight',              'body_weight',      'kg',     0.5, 300),
  ('8302-2',  'Body height',              'body_height',      'cm',     30,  250)
ON CONFLICT (loinc_code) DO NOTHING;

-- ---------------------------------------------------------------------------
-- observations  ->  FHIR Observation
-- One row per measured value. Blood pressure is two rows (systolic, diastolic) sharing a
-- panel_id, which maps to one FHIR blood-pressure Observation (85354-9) with two components.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS observations (
  id                  SERIAL PRIMARY KEY,                          -- Observation.id
  fhir_resource_type  TEXT NOT NULL DEFAULT 'Observation'
                      CHECK (fhir_resource_type = 'Observation'),  -- Observation.resourceType
  patient_id          INTEGER NOT NULL REFERENCES patients(id),    -- Observation.subject -> Patient
  status              TEXT NOT NULL DEFAULT 'final'
                      CHECK (status IN ('preliminary', 'final', 'amended', 'entered-in-error')),
                                                                   -- Observation.status (required in FHIR)
  category            TEXT NOT NULL DEFAULT 'vital-signs',         -- Observation.category
  observation_type    TEXT NOT NULL,                               -- the lab's free-text name, kept for the starter
  loinc_code          TEXT NOT NULL REFERENCES observation_codes(loinc_code),
                                                                   -- Observation.code (coded, required)
  value               TEXT NOT NULL,                               -- Observation.value[x] as sent
  value_quantity      NUMERIC GENERATED ALWAYS AS (
                        CASE WHEN value ~ '^-?[0-9]+(\.[0-9]+)?$' THEN value::numeric END
                      ) STORED,                                    -- Observation.valueQuantity.value
  unit                TEXT NOT NULL,                               -- Observation.valueQuantity.code (UCUM)
  panel_id            UUID,                                        -- groups BP components into one 85354-9 panel
  recorded_by         TEXT NOT NULL,                               -- Observation.performer
  recorded_at         TIMESTAMPTZ NOT NULL DEFAULT now()           -- Observation.effectiveDateTime
);

CREATE INDEX IF NOT EXISTS observations_patient_time_idx
  ON observations (patient_id, recorded_at DESC);

-- Value, unit and name must agree with the coded terminology.
-- (Generated columns are computed after BEFORE triggers, so the value is parsed here.)
CREATE OR REPLACE FUNCTION observations_check_code() RETURNS trigger AS $$
DECLARE
  c observation_codes;
  v NUMERIC := CASE WHEN NEW.value ~ '^-?[0-9]+(\.[0-9]+)?$' THEN NEW.value::numeric END;
BEGIN
  SELECT * INTO c FROM observation_codes WHERE loinc_code = NEW.loinc_code;
  IF NOT FOUND THEN
    RAISE EXCEPTION '% is not a LOINC code in observation_codes (free-text codes are not allowed)', NEW.loinc_code;
  END IF;
  IF NEW.unit IS DISTINCT FROM c.ucum_unit THEN
    RAISE EXCEPTION 'LOINC % must use UCUM unit %, got %', NEW.loinc_code, c.ucum_unit, NEW.unit;
  END IF;
  IF NEW.observation_type IS DISTINCT FROM c.short_name THEN
    RAISE EXCEPTION 'LOINC % is %, not %', NEW.loinc_code, c.short_name, NEW.observation_type;
  END IF;
  IF v IS NULL OR v NOT BETWEEN c.min_value AND c.max_value THEN
    RAISE EXCEPTION '% = % is outside the plausible range % to % %',
      c.display, NEW.value, c.min_value, c.max_value, c.ucum_unit;
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS observations_coded ON observations;
CREATE TRIGGER observations_coded
  BEFORE INSERT OR UPDATE ON observations
  FOR EACH ROW EXECUTE FUNCTION observations_check_code();

-- ---------------------------------------------------------------------------
-- access_log  ->  FHIR AuditEvent (simplified)
-- No foreign key on patient_id on purpose: the log must record attempts on records that do
-- not exist or that the caller may not see, and must survive if a patient record is removed.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS access_log (
  id              SERIAL PRIMARY KEY,                              -- AuditEvent.id
  accessed_by     TEXT NOT NULL,                                   -- AuditEvent.agent.who
  role            TEXT,                                            -- AuditEvent.agent.role
  patient_id      INTEGER,                                         -- AuditEvent.entity.what -> Patient
  action          TEXT NOT NULL
                  CHECK (action IN ('read', 'write', 'delete')),   -- AuditEvent.action (R / C,U / D)
  outcome         TEXT NOT NULL DEFAULT 'success'
                  CHECK (outcome IN ('success', 'denied', 'not-found', 'error')),
                                                                   -- AuditEvent.outcome
  purpose_of_use  TEXT NOT NULL DEFAULT 'TREAT'
                  CHECK (purpose_of_use IN ('TREAT', 'PUBHLTH', 'HOPERAT')),
                                                                   -- AuditEvent.purposeOfEvent (HL7 v3)
  source_ip       INET,                                            -- AuditEvent.agent.network
  accessed_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),  -- AuditEvent.recorded
  prev_hash       TEXT,                                            -- tamper evidence: hash of the previous entry
  entry_hash      TEXT                                             -- sha256(prev_hash || this entry)
);

-- Schema upgrade for databases created from the first draft (columns added in place).
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS role TEXT;
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS outcome TEXT NOT NULL DEFAULT 'success';
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS purpose_of_use TEXT NOT NULL DEFAULT 'TREAT';
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS source_ip INET;
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS prev_hash TEXT;
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS entry_hash TEXT;

CREATE INDEX IF NOT EXISTS access_log_patient_time_idx
  ON access_log (patient_id, accessed_at DESC);

-- Each entry is chained to the one before it. Editing or deleting an old row (e.g. by
-- someone who bypasses the trigger below with a superuser) breaks every later hash, and
-- verify_access_log() finds where. Entries are serialised with an advisory lock.
CREATE OR REPLACE FUNCTION access_log_chain() RETURNS trigger AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(843);
  SELECT entry_hash INTO NEW.prev_hash FROM access_log ORDER BY id DESC LIMIT 1;
  NEW.entry_hash := encode(digest(
    coalesce(NEW.prev_hash, '') || '|' || NEW.accessed_by || '|' || coalesce(NEW.role, '') || '|' ||
    coalesce(NEW.patient_id::text, '') || '|' || NEW.action || '|' || NEW.outcome || '|' ||
    NEW.purpose_of_use || '|' || NEW.accessed_at::text, 'sha256'), 'hex');
  RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

DROP TRIGGER IF EXISTS access_log_hash_chain ON access_log;
CREATE TRIGGER access_log_hash_chain
  BEFORE INSERT ON access_log
  FOR EACH ROW EXECUTE FUNCTION access_log_chain();

-- The audit log is append-only: entries can be added, never edited, removed or truncated.
CREATE OR REPLACE FUNCTION access_log_is_append_only() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'access_log is append-only: % is not allowed', TG_OP;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS access_log_no_update_delete ON access_log;
CREATE TRIGGER access_log_no_update_delete
  BEFORE UPDATE OR DELETE ON access_log
  FOR EACH ROW EXECUTE FUNCTION access_log_is_append_only();

DROP TRIGGER IF EXISTS access_log_no_truncate ON access_log;
CREATE TRIGGER access_log_no_truncate
  BEFORE TRUNCATE ON access_log
  FOR EACH STATEMENT EXECUTE FUNCTION access_log_is_append_only();

-- Recomputes the chain; returns the first entry whose hash does not match (none = intact).
CREATE OR REPLACE FUNCTION verify_access_log()
RETURNS TABLE (first_bad_id INTEGER, entries_checked BIGINT) AS $$
  WITH ordered AS (
    SELECT id, entry_hash, prev_hash,
           lag(entry_hash) OVER (ORDER BY id) AS expected_prev,
           encode(digest(
             coalesce(prev_hash, '') || '|' || accessed_by || '|' || coalesce(role, '') || '|' ||
             coalesce(patient_id::text, '') || '|' || action || '|' || outcome || '|' ||
             purpose_of_use || '|' || accessed_at::text, 'sha256'), 'hex') AS recomputed
    FROM access_log WHERE entry_hash IS NOT NULL
  )
  SELECT (SELECT min(id) FROM ordered
          WHERE recomputed <> entry_hash OR prev_hash IS DISTINCT FROM expected_prev),
         (SELECT count(*) FROM ordered);
$$ LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public;


-- ====================================================== rbac.sql ======================================================
-- HealthConnect Ghana: roles enforced by the database (Lab 6 homework: RBAC, built, not sketched)
-- Run after schema.sql. The service connects as one login and, for every request, switches to
-- the caller's role inside the transaction:
--   BEGIN; SET LOCAL ROLE hc_chv; SELECT set_config('app.user_id', 'chv-042', true);
--          SELECT set_config('app.district', 'Shai-Osudoku', true); ...; COMMIT;
-- so even a bug in the API cannot show a CHV another district's patients: Postgres refuses.
--
--                     patients            observations        access_log        surveillance
--   hc_chv            own district R/W    own district R/W    append only       -
--   hc_clinician      all R/W             all R/W             append only       read
--   hc_ghs_official   -                   -                   append only       read (anonymised)
--   hc_auditor        -                   -                   read + append     -
-- Nobody can UPDATE, DELETE or TRUNCATE access_log (schema.sql triggers), and nobody gets DELETE
-- on clinical data: a wrong reading is marked 'entered-in-error', as FHIR does.


DO $$
DECLARE r TEXT;
BEGIN
  FOREACH r IN ARRAY ARRAY['hc_chv', 'hc_clinician', 'hc_ghs_official', 'hc_auditor'] LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
      EXECUTE format('CREATE ROLE %I NOLOGIN', r);
    END IF;
  END LOOP;
END $$;

-- The service's login may switch into these roles (and only these).
GRANT hc_chv, hc_clinician, hc_ghs_official, hc_auditor TO CURRENT_USER;

REVOKE ALL ON patients, observations, consents, patient_identifiers, access_log, observation_codes
  FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
GRANT USAGE ON SCHEMA public TO hc_chv, hc_clinician, hc_ghs_official, hc_auditor;

-- Clinical roles: read and write, never delete.
GRANT SELECT, INSERT, UPDATE ON patients, observations, consents, patient_identifiers
  TO hc_chv, hc_clinician;
GRANT SELECT ON observation_codes TO hc_chv, hc_clinician;
GRANT USAGE ON SEQUENCE patients_id_seq, observations_id_seq, consents_id_seq
  TO hc_chv, hc_clinician;

-- Everyone's actions are logged, so everyone may append; only the auditor may read.
GRANT INSERT ON access_log TO hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
GRANT USAGE ON SEQUENCE access_log_id_seq TO hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
GRANT SELECT ON access_log TO hc_auditor;
GRANT EXECUTE ON FUNCTION verify_access_log() TO hc_auditor;
REVOKE EXECUTE ON FUNCTION verify_access_log() FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- Row-level security: a CHV only sees patients in the district set for the request.
-- ---------------------------------------------------------------------------
ALTER TABLE patients ENABLE ROW LEVEL SECURITY;
ALTER TABLE observations ENABLE ROW LEVEL SECURITY;
ALTER TABLE consents ENABLE ROW LEVEL SECURITY;
ALTER TABLE patient_identifiers ENABLE ROW LEVEL SECURITY;

CREATE OR REPLACE FUNCTION app_district() RETURNS TEXT
  LANGUAGE sql STABLE AS $$ SELECT current_setting('app.district', true) $$;

DROP POLICY IF EXISTS chv_own_district ON patients;
CREATE POLICY chv_own_district ON patients TO hc_chv
  USING (district = app_district()) WITH CHECK (district = app_district());
DROP POLICY IF EXISTS clinician_all ON patients;
CREATE POLICY clinician_all ON patients TO hc_clinician USING (true) WITH CHECK (true);

-- Child tables follow their patient: visible only if the patient row is visible.
DROP POLICY IF EXISTS follows_patient ON observations;
CREATE POLICY follows_patient ON observations TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));
DROP POLICY IF EXISTS follows_patient ON consents;
CREATE POLICY follows_patient ON consents TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));
DROP POLICY IF EXISTS follows_patient ON patient_identifiers;
CREATE POLICY follows_patient ON patient_identifiers TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));

-- ---------------------------------------------------------------------------
-- Surveillance for GHS officials: counts per district and week, never names, ids or exact
-- dates. Cells under 5 patients are suppressed so a small village's patient can't be
-- singled out (Act 843 s.24(3): statistical records must be protected from identification).
-- The view runs with its owner's rights, so officials need no access to the tables.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW surveillance_weekly AS
WITH readings AS (
  SELECT p.district,
         date_trunc('week', o.recorded_at)::date AS week,
         o.patient_id,
         o.loinc_code,
         o.value_quantity
  FROM observations o JOIN patients p ON p.id = o.patient_id
  WHERE o.status <> 'entered-in-error'
),
per_cell AS (
  SELECT district, week,
         count(DISTINCT patient_id) AS patients_seen,
         count(DISTINCT patient_id) FILTER (
           WHERE (loinc_code = '8480-6' AND value_quantity >= 140)
              OR (loinc_code = '8462-4' AND value_quantity >= 90)) AS patients_high_bp,
         count(DISTINCT patient_id) FILTER (
           WHERE loinc_code = '8310-5' AND value_quantity >= 38) AS patients_fever
  FROM readings GROUP BY district, week
)
SELECT district, week,
       CASE WHEN patients_seen < 5 THEN NULL ELSE patients_seen END AS patients_seen,
       CASE WHEN patients_seen < 5 THEN NULL ELSE patients_high_bp END AS patients_high_bp,
       CASE WHEN patients_seen < 5 THEN NULL ELSE patients_fever END AS patients_fever,
       patients_seen < 5 AS suppressed
FROM per_cell;

GRANT SELECT ON surveillance_weekly TO hc_ghs_official, hc_clinician;
