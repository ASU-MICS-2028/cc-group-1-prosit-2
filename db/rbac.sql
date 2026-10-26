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

BEGIN;

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

COMMIT;
