-- Migration 8: patients can sign in, and workers are added from the app.
--
--   users.role 'patient'  a patient account. It is the phone number a CHV registered on one or more patient records (a household
--                         often shares one phone), made the first time someone asks for a sign-in code with it. It reads only the
--                         records with that number, through the API's own login, never through the workers' database roles.
--   users.added_by        who added a worker from the Health workers page (null for the demo workers and patient accounts).
--
-- Additive, and safe to run again.

-- The role rule from migration 2 had no name of its own, so Postgres called it users_role_check.
ALTER TABLE users DROP CONSTRAINT IF EXISTS users_role_check;
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'users_role_allowed') THEN
    ALTER TABLE users ADD CONSTRAINT users_role_allowed
      CHECK (role IN ('chv', 'nurse', 'doctor', 'district_officer', 'admin', 'patient'));
  END IF;
END $$;

ALTER TABLE users ADD COLUMN IF NOT EXISTS added_by TEXT;

CREATE INDEX IF NOT EXISTS patients_phone_idx ON patients (phone) WHERE phone IS NOT NULL;
