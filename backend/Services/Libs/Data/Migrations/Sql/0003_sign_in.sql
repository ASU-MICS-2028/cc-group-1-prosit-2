-- Migration 3: what sign-in needs.
--
--   users.demo   true for the synthetic demo workers; only they can use the fixed demo code (never a real worker)
--   auth_keys    the secret the API signs sign-in tokens with. One row, made by the API the first time it starts (random 32 bytes),
--                so every API container and every restart signs and checks with the same key and no new AWS secret is needed.
--                No database role gets a grant: only the API's own login (the table owner) can read it.
--
-- Additive, and safe to run again.

ALTER TABLE users ADD COLUMN IF NOT EXISTS demo BOOLEAN NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS auth_keys (
  id          SMALLINT PRIMARY KEY DEFAULT 1 CHECK (id = 1),            -- one row only
  signing_key BYTEA NOT NULL CHECK (octet_length(signing_key) >= 32),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

REVOKE ALL ON auth_keys FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
