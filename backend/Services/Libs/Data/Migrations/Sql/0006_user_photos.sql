-- Migration 6: a health worker's profile photo, so it follows them to any phone they sign in on.
--
--   user_photos   one small JPEG per worker (the app shrinks it to 288 px, ~20-40 KB; the database refuses more than 128 KB).
--                 Read and written only by the API's own login, for the signed-in worker's own row (GET/PUT/DELETE /api/auth/me/photo).
--                 No database role gets a grant. Not health data, so it is outside the patient tables and their row-level security.
--
-- Additive, and safe to run again.

CREATE TABLE IF NOT EXISTS user_photos (
  user_id    TEXT PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
  photo      BYTEA NOT NULL CHECK (octet_length(photo) BETWEEN 1 AND 131072),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

REVOKE ALL ON user_photos FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
