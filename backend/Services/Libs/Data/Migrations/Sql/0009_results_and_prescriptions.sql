-- Migration 9: test results (with photos of lab slips and X-ray films) and prescriptions.
--
--   test_results   A test the patient needs or had. Most villages have no laboratory or X-ray machine, so a test is often *requested*
--                  first (the patient is sent to a facility that has one) and *resulted* later, when the patient comes back with the
--                  paper slip or the film and the CHV photographs it. Quick tests a CHV does on the spot (malaria RDT, blood sugar,
--                  pregnancy test) are saved as resulted straight away. FHIR: ServiceRequest (requested) + DiagnosticReport (resulted).
--   prescriptions  Medicines a doctor prescribes, given out by a pharmacy or a health centre. The code (RX-XXXX-XXXX) is the first
--                  8 hex digits of the id, so the phone can show it offline and the pharmacy can read it over the phone.
--                  FHIR: MedicationRequest.
--   media          Photos can now belong to a test result ('result-photo'), and other workers' phones can fetch them.
--
-- Additive, and safe to run again.

CREATE TABLE IF NOT EXISTS test_results (
  id           UUID PRIMARY KEY,                                     -- from the phone
  patient_id   INTEGER NOT NULL REFERENCES patients(id),
  category     TEXT NOT NULL CHECK (category IN ('rapid', 'lab', 'imaging')),
                                                                   -- rapid = done on the spot; lab; imaging = X-ray, ultrasound
  test         TEXT NOT NULL CHECK (length(trim(test)) BETWEEN 1 AND 120),
  status       TEXT NOT NULL CHECK (status IN ('requested', 'resulted', 'cancelled')),
  facility     TEXT CHECK (length(facility) <= 200),               -- where it is (to be) done
  result       TEXT CHECK (length(result) <= 2000),                -- what the slip or report says
  flag         TEXT CHECK (flag IN ('normal', 'abnormal')),
  notes        TEXT CHECK (length(notes) <= 2000),
  requested_at TIMESTAMPTZ NOT NULL,
  resulted_at  TIMESTAMPTZ,
  created_by   TEXT NOT NULL,                                        -- users.id
  updated_by   TEXT,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  version      INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1),
  CONSTRAINT test_results_resulted_has_time CHECK ((status = 'resulted') = (resulted_at IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS test_results_patient_idx ON test_results (patient_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS test_results_updated_idx ON test_results (updated_at);

CREATE TABLE IF NOT EXISTS prescriptions (
  id             UUID PRIMARY KEY,                                   -- from the phone
  patient_id     INTEGER NOT NULL REFERENCES patients(id),
  appointment_id UUID REFERENCES appointments(id),                   -- the referral or teleconsult it came from, if any
  code           TEXT NOT NULL UNIQUE CHECK (code ~ '^RX-[0-9A-F]{4}-[0-9A-F]{4}$'),
  items          JSONB NOT NULL CHECK (jsonb_typeof(items) = 'array' AND jsonb_array_length(items) BETWEEN 1 AND 10),
                                                                   -- [{ medicine, dose, frequency, days, instructions }]
  notes          TEXT CHECK (length(notes) <= 2000),
  status         TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'dispensed', 'cancelled')),
  prescribed_by  TEXT NOT NULL,                                      -- the doctor (users.id), from the sign-in token
  prescriber     TEXT NOT NULL CHECK (length(trim(prescriber)) > 0), -- the doctor's name as the pharmacy reads it
  prescribed_at  TIMESTAMPTZ NOT NULL,
  dispensed_by   TEXT,                                               -- the worker who recorded that it was given out
  dispensed_at   TIMESTAMPTZ,
  pharmacy       TEXT CHECK (length(pharmacy) <= 200),               -- who gave it out
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  version        INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1),
  CONSTRAINT prescriptions_dispensed_together
    CHECK ((status = 'dispensed') = (dispensed_at IS NOT NULL AND dispensed_by IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS prescriptions_patient_idx ON prescriptions (patient_id, prescribed_at DESC);
CREATE INDEX IF NOT EXISTS prescriptions_updated_idx ON prescriptions (updated_at);

-- Photos of a lab slip or an X-ray film belong to a test result.
ALTER TABLE media DROP CONSTRAINT IF EXISTS media_owner_entity_check;
ALTER TABLE media DROP CONSTRAINT IF EXISTS media_kind_check;
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'media_owner_allowed') THEN
    ALTER TABLE media ADD CONSTRAINT media_owner_allowed
      CHECK (owner_entity IN ('patient', 'vitals', 'appointment', 'result'));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'media_kind_allowed') THEN
    ALTER TABLE media ADD CONSTRAINT media_kind_allowed
      CHECK (kind IN ('consent-audio', 'consent-signature', 'consent-photo', 'wound-photo', 'result-photo'));
  END IF;
END $$;

-- Same rules as the other clinical tables: each follows its patient (CHVs by district, clinicians everywhere), never delete.
-- Only a doctor may write a prescription; the API checks that, because nurses and doctors share the clinician database role.
ALTER TABLE test_results  ENABLE ROW LEVEL SECURITY;
ALTER TABLE prescriptions ENABLE ROW LEVEL SECURITY;

REVOKE ALL ON test_results, prescriptions FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
GRANT SELECT, INSERT, UPDATE ON test_results TO hc_chv, hc_clinician;
GRANT SELECT, UPDATE ON prescriptions TO hc_chv;
GRANT SELECT, INSERT, UPDATE ON prescriptions TO hc_clinician;

DROP POLICY IF EXISTS follows_patient ON test_results;
CREATE POLICY follows_patient ON test_results TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));
DROP POLICY IF EXISTS follows_patient ON prescriptions;
CREATE POLICY follows_patient ON prescriptions TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));
