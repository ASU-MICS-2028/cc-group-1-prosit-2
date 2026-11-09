-- Migration 4: what the app's own audit trail needs on the server's access_log.
-- The phone records who saw or changed what while it is offline and uploads it later. The log keeps its tamper-evident chain
-- (the hash covers who, role, patient, action, outcome, purpose and time, none of which change here); these columns only add
-- the phone's own references, so an upload that is sent twice is stored once, and an auditor can find events for a patient
-- by the id the phone uses.

ALTER TABLE access_log ADD COLUMN IF NOT EXISTS client_event_id UUID;       -- the id the phone gave the event: makes uploads repeatable
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS patient_uuid    UUID;       -- the patient's id on the phone (patients.fhir_id), even if the patient is not visible to the sender
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS entity          TEXT;       -- patient, vitals, appointment, ... or "page" for a refused visit
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS entity_id       TEXT;
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS detail          TEXT;       -- e.g. the address of a refused page
ALTER TABLE access_log ADD COLUMN IF NOT EXISTS client_at       TIMESTAMPTZ; -- when it happened on the phone (accessed_at is when the server recorded it)

CREATE UNIQUE INDEX IF NOT EXISTS access_log_client_event_idx
  ON access_log (client_event_id) WHERE client_event_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS access_log_patient_uuid_idx
  ON access_log (patient_uuid, accessed_at DESC) WHERE patient_uuid IS NOT NULL;
