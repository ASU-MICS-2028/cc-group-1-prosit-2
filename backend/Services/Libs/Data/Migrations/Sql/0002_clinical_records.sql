-- Migration 2: what the app sends that the first draft of the schema did not have.
--
--   patients       given and family name, community, version, who saved it
--   consents       the proof of consent: statement version, language, witness, the recording or photo and its SHA-256
--   vitals         one reading as the phone made it (the app's own id), its measurements stay in observations
--   appointments   visits, referrals and teleconsults, with the referral status a nurse or doctor sets
--   users          the health workers who may sign in
--   login_codes    the SMS codes (stored hashed)
--   media          the files the phone uploads straight to S3 (consent recordings, wound photos)
--
-- Shapes follow docs/frontend-needs.md. Like migration 1, it can be run again without harm.
-- Nothing here is deleted or renamed, so the previous version of the API keeps working if a release is rolled back.

-- ---------------------------------------------------------------------------
-- patients: the fields the app sends. full_name stays (the API fills it from given + family name).
-- ---------------------------------------------------------------------------
ALTER TABLE patients
  ADD COLUMN IF NOT EXISTS given_name  TEXT CHECK (length(trim(given_name)) > 0),
  ADD COLUMN IF NOT EXISTS family_name TEXT CHECK (length(trim(family_name)) > 0),
  ADD COLUMN IF NOT EXISTS community   TEXT CHECK (length(trim(community)) > 0),
                                                                   -- the village or town; district alone is too coarse to find a person
  ADD COLUMN IF NOT EXISTS version     INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1),
                                                                   -- the row version the API checks on every save (409 when stale)
  ADD COLUMN IF NOT EXISTS created_by  TEXT,                        -- the health worker (users.id) who registered the patient
  ADD COLUMN IF NOT EXISTS updated_by  TEXT;

CREATE INDEX IF NOT EXISTS patients_updated_idx ON patients (updated_at);

-- ---------------------------------------------------------------------------
-- consents: the proof. Act 843 s.20: how, when, which wording, in which language, witnessed by whom.
-- ---------------------------------------------------------------------------
ALTER TABLE consents
  ADD COLUMN IF NOT EXISTS statement_version TEXT,                   -- the wording that was read out, e.g. 'v1'
  ADD COLUMN IF NOT EXISTS language          TEXT CHECK (language IN ('en', 'tw', 'ee', 'dag', 'ha')),
  ADD COLUMN IF NOT EXISTS witness           TEXT,                   -- who heard the patient agree; needed when nothing was recorded
  ADD COLUMN IF NOT EXISTS evidence_media_id UUID,                   -- media.id of the recording, signature or thumbprint photo
  ADD COLUMN IF NOT EXISTS evidence_key      TEXT,                   -- where it is in the media bucket
  ADD COLUMN IF NOT EXISTS evidence_sha256   TEXT CHECK (evidence_sha256 ~ '^[0-9a-f]{64}$'),
                                                                   -- proves later that the file was not changed
  ADD COLUMN IF NOT EXISTS evidence_mime     TEXT;

-- A new consent needs proof: a file, or a named witness. NOT VALID checks every new or changed row but leaves rows
-- from before this migration alone (the Lab 6 sample data has neither).
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'consents_has_evidence') THEN
    ALTER TABLE consents ADD CONSTRAINT consents_has_evidence
      CHECK (evidence_sha256 IS NOT NULL OR witness IS NOT NULL) NOT VALID;
  END IF;
END $$;

-- ---------------------------------------------------------------------------
-- vitals  ->  the phone's Vitals form. Each measurement is an observations row (LOINC); this row groups them.
-- The phone makes the id, so saving the same reading twice finds the same row.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS vitals (
  id          UUID PRIMARY KEY,                                      -- from the phone
  patient_id  INTEGER NOT NULL REFERENCES patients(id),
  taken_at    TIMESTAMPTZ NOT NULL,                                  -- when it was measured (on the phone)
  notes       TEXT CHECK (length(notes) <= 2000),
  recorded_by TEXT NOT NULL,                                         -- users.id, from the sign-in token
  version     INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()                    -- when the server stored it
);

CREATE INDEX IF NOT EXISTS vitals_patient_time_idx ON vitals (patient_id, taken_at DESC);
CREATE INDEX IF NOT EXISTS vitals_created_idx ON vitals (created_at);

ALTER TABLE observations ADD COLUMN IF NOT EXISTS vitals_id UUID REFERENCES vitals(id);
CREATE INDEX IF NOT EXISTS observations_vitals_idx ON observations (vitals_id);
-- One reading has at most one value per measurement (no two systolic values for the same reading).
CREATE UNIQUE INDEX IF NOT EXISTS observations_one_per_reading_and_code
  ON observations (vitals_id, loinc_code) WHERE vitals_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- appointments  ->  FHIR Appointment (visits) and ServiceRequest (referrals and teleconsults)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS appointments (
  id              UUID PRIMARY KEY,                                  -- from the phone
  patient_id      INTEGER NOT NULL REFERENCES patients(id),
  type            TEXT NOT NULL
                  CHECK (type IN ('follow_up', 'antenatal', 'postnatal', 'referral', 'teleconsult')),
  scheduled_at    TIMESTAMPTZ NOT NULL,
  facility        TEXT NOT NULL CHECK (length(trim(facility)) > 0),  -- free text for the MVP
  reason          TEXT NOT NULL DEFAULT '' CHECK (length(reason) <= 2000),
  priority        TEXT NOT NULL DEFAULT 'routine' CHECK (priority IN ('routine', 'urgent', 'emergency')),
  status          TEXT NOT NULL DEFAULT 'scheduled'
                  CHECK (status IN ('scheduled', 'completed', 'missed', 'cancelled')),
  -- Only a referral or a teleconsult has a referral status: requested -> accepted or declined -> seen.
  referral_status TEXT CHECK (referral_status IN ('requested', 'accepted', 'declined', 'seen')),
  handled_by      TEXT,                                              -- the nurse or doctor (users.id) who accepted or declined it
  handled_at      TIMESTAMPTZ,
  created_by      TEXT,                                              -- users.id
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  version         INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1),
  CONSTRAINT appointments_referral_status_only_for_referrals
    CHECK (referral_status IS NULL OR type IN ('referral', 'teleconsult')),
  CONSTRAINT appointments_handled_together
    CHECK ((handled_by IS NULL) = (handled_at IS NULL))
);

CREATE INDEX IF NOT EXISTS appointments_patient_idx ON appointments (patient_id, scheduled_at);
CREATE INDEX IF NOT EXISTS appointments_updated_idx ON appointments (updated_at);
-- The referral inbox: open referrals, emergencies first.
CREATE INDEX IF NOT EXISTS appointments_open_referrals_idx ON appointments (priority, scheduled_at)
  WHERE referral_status IN ('requested', 'accepted');

-- ---------------------------------------------------------------------------
-- users: the health workers who may sign in. Only the API's own login reads this table (no role below gets a grant).
-- The id is text so the demo workers keep readable ids ('chv-042') in the audit trail.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
  id         TEXT PRIMARY KEY CHECK (length(id) BETWEEN 1 AND 64),
  phone      TEXT NOT NULL UNIQUE CHECK (phone ~ '^\+233[0-9]{9}$'), -- E.164, Ghana
  name       TEXT NOT NULL CHECK (length(trim(name)) > 0),
  role       TEXT NOT NULL CHECK (role IN ('chv', 'nurse', 'doctor', 'district_officer', 'admin')),
  district   TEXT NOT NULL CHECK (length(trim(district)) > 0),
  active     BOOLEAN NOT NULL DEFAULT true,                          -- false = signed-out everywhere, cannot sign in
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- login_codes: the 6-digit SMS code, kept only as a hash. Expires after 10 minutes, 5 tries (enforced by the API).
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS login_codes (
  id         BIGSERIAL PRIMARY KEY,
  phone      TEXT NOT NULL CHECK (phone ~ '^\+233[0-9]{9}$'),
  code_hash  TEXT NOT NULL CHECK (length(code_hash) BETWEEN 32 AND 128),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at TIMESTAMPTZ NOT NULL,
  attempts   INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  used_at    TIMESTAMPTZ,
  CONSTRAINT login_codes_expires_after_created CHECK (expires_at > created_at)
);

CREATE INDEX IF NOT EXISTS login_codes_phone_idx ON login_codes (phone, created_at DESC);

-- ---------------------------------------------------------------------------
-- media: a file the phone uploads straight to S3 with a 5-minute link the API issues. The API records that it issued
-- the link; the file's bytes never pass through the API or this database.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS media (
  id           UUID PRIMARY KEY,                                     -- from the phone
  patient_id   INTEGER NOT NULL REFERENCES patients(id),
  owner_entity TEXT NOT NULL CHECK (owner_entity IN ('patient', 'vitals', 'appointment')),
  owner_id     UUID NOT NULL,                                        -- the record the file belongs to
  kind         TEXT NOT NULL
               CHECK (kind IN ('consent-audio', 'consent-signature', 'consent-photo', 'wound-photo')),
  mime         TEXT NOT NULL CHECK (mime LIKE 'audio/%' OR mime IN ('image/png', 'image/jpeg')),
  size_bytes   INTEGER NOT NULL CHECK (size_bytes BETWEEN 1 AND 2097152),   -- at most 2 MB
  sha256       TEXT NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
  s3_key       TEXT NOT NULL UNIQUE,                                 -- media/{patient fhir_id}/{id}
  requested_by TEXT NOT NULL,                                        -- users.id
  requested_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS media_patient_idx ON media (patient_id);
CREATE INDEX IF NOT EXISTS media_owner_idx ON media (owner_entity, owner_id);

-- ---------------------------------------------------------------------------
-- Who may do what with the new tables (same rules as migration 1: CHVs by district, clinicians everywhere, never delete).
-- ---------------------------------------------------------------------------
ALTER TABLE vitals       ENABLE ROW LEVEL SECURITY;
ALTER TABLE appointments ENABLE ROW LEVEL SECURITY;
ALTER TABLE media        ENABLE ROW LEVEL SECURITY;

REVOKE ALL ON vitals, appointments, media FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
GRANT SELECT, INSERT, UPDATE ON vitals, appointments, media TO hc_chv, hc_clinician;

-- Each follows its patient: visible only when the patient row is visible to the caller.
DROP POLICY IF EXISTS follows_patient ON vitals;
CREATE POLICY follows_patient ON vitals TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));
DROP POLICY IF EXISTS follows_patient ON appointments;
CREATE POLICY follows_patient ON appointments TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));
DROP POLICY IF EXISTS follows_patient ON media;
CREATE POLICY follows_patient ON media TO hc_chv, hc_clinician
  USING (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id))
  WITH CHECK (EXISTS (SELECT 1 FROM patients p WHERE p.id = patient_id));

-- users and login_codes get no grant at all: only the API's own login (the table owner) can read them.
REVOKE ALL ON users, login_codes FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;

-- The auditor ("admin" in the app) also opens the district summary page (frontend: /surveillance for district_officer and admin).
GRANT SELECT ON surveillance_weekly TO hc_auditor;
