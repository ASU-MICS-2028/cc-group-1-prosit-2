-- A COPY of Liza Aikins' first draft of the schema (the Lab 6 starter), kept only as a test fixture: Data.Tests starts from a database
-- made with it to prove migration 0 upgrades such a database without losing data. Do not use it to create a real database.

-- HealthConnect Ghana: FHIR-aligned patient data model (Lab 6, Part C)
-- Team Godabeg | ICS 534 | PostgreSQL 17 (healthconnect-db)
-- Synthetic data only. Shaped like FHIR resources; this is not a FHIR server.

BEGIN;

-- ---------------------------------------------------------------------------
-- patients  ->  FHIR Patient
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS patients (
  id                  SERIAL PRIMARY KEY,                          -- Patient.id
  fhir_resource_type  TEXT NOT NULL DEFAULT 'Patient'
                      CHECK (fhir_resource_type = 'Patient'),      -- Patient.resourceType
  full_name           TEXT NOT NULL,                               -- Patient.name.text
  phone               TEXT,                                        -- Patient.telecom (optional: not every patient has a phone)
  date_of_birth       DATE,                                        -- Patient.birthDate
  gender              TEXT,                                        -- Patient.gender
  region              TEXT,                                        -- Patient.address.state
  preferred_language  TEXT,                                        -- Patient.communication.language
  nhis_number         TEXT,                                        -- Patient.identifier (optional; see data-minimisation finding)
  consent_given_at    TIMESTAMPTZ,                                 -- NULL until the patient (or CHV on their behalf) consents
  consent_method      TEXT
                      CHECK (consent_method IN ('spoken', 'thumbprint', 'signature')),
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  -- consent is recorded completely or not at all: a time without a method (or the reverse) is rejected
  CONSTRAINT consent_recorded_together
    CHECK ((consent_given_at IS NULL) = (consent_method IS NULL))
);

-- ---------------------------------------------------------------------------
-- observations  ->  FHIR Observation
-- One row per measured value. Blood pressure is stored as two rows
-- (systolic, diastolic), each with its own LOINC code.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS observations (
  id                  SERIAL PRIMARY KEY,                          -- Observation.id
  fhir_resource_type  TEXT NOT NULL DEFAULT 'Observation'
                      CHECK (fhir_resource_type = 'Observation'),  -- Observation.resourceType
  patient_id          INTEGER NOT NULL REFERENCES patients(id),    -- Observation.subject -> Patient
  status              TEXT NOT NULL DEFAULT 'final'
                      CHECK (status IN ('preliminary', 'final', 'amended', 'entered-in-error')),
                                                                   -- Observation.status (required in FHIR)
  observation_type    TEXT NOT NULL,                               -- human-readable, e.g. 'systolic_bp' (Observation.code.text)
  loinc_code          TEXT,                                        -- Observation.code.coding (system = http://loinc.org)
  value               TEXT NOT NULL,                               -- Observation.value[x]
  unit                TEXT,                                        -- Observation.valueQuantity.unit (UCUM, e.g. 'mm[Hg]', 'Cel', 'kg')
  recorded_by         TEXT NOT NULL,                               -- Observation.performer (free text for now)
  recorded_at         TIMESTAMPTZ NOT NULL DEFAULT now()           -- Observation.effectiveDateTime
);

CREATE INDEX IF NOT EXISTS observations_patient_time_idx
  ON observations (patient_id, recorded_at DESC);

-- ---------------------------------------------------------------------------
-- access_log  ->  FHIR AuditEvent (simplified)
-- No foreign key on patient_id on purpose: the log must record attempts on
-- records that do not exist (the starter GET logs before its 404 check) and
-- must survive if a patient record is ever removed.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS access_log (
  id           SERIAL PRIMARY KEY,                                 -- AuditEvent.id
  accessed_by  TEXT NOT NULL,                                      -- AuditEvent.agent.who
  patient_id   INTEGER,                                            -- AuditEvent.entity.what -> Patient
  action       TEXT NOT NULL
               CHECK (action IN ('read', 'write', 'delete')),      -- AuditEvent.action (R / C,U / D)
  accessed_at  TIMESTAMPTZ NOT NULL DEFAULT now()                  -- AuditEvent.recorded
);

CREATE INDEX IF NOT EXISTS access_log_patient_time_idx
  ON access_log (patient_id, accessed_at DESC);

-- The audit log is append-only: entries can be added, never edited or removed.
CREATE OR REPLACE FUNCTION access_log_is_append_only() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'access_log is append-only: % is not allowed', TG_OP;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS access_log_no_update_delete ON access_log;
CREATE TRIGGER access_log_no_update_delete
  BEFORE UPDATE OR DELETE ON access_log
  FOR EACH ROW EXECUTE FUNCTION access_log_is_append_only();

COMMIT;
