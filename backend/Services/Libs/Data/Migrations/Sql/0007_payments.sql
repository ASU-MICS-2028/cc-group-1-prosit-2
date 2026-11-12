-- Migration 7: consultation fees paid by mobile money (Paystack). Brief, week 2: "payment integration for consultation fees via
-- mobile money".
--
--   payments   one row per checkout started for a teleconsult. The reference is ours (sent to Paystack); status only becomes 'paid'
--              after the API has checked the transaction with Paystack itself (never on the phone's word). Amounts are in pesewas.
--              district is copied from the patient when the checkout starts, so the district officer's list needs no patient table.
--              Read and written only by the API's own login: no database role gets a grant. No patient name or phone is kept here.
--
-- Additive, and safe to run again.

CREATE TABLE IF NOT EXISTS payments (
  reference      TEXT PRIMARY KEY CHECK (reference ~ '^hc-[a-z0-9-]{8,80}$'),
  appointment_id UUID NOT NULL REFERENCES appointments (id),
  district       TEXT NOT NULL,
  amount_pesewas INTEGER NOT NULL CHECK (amount_pesewas > 0),
  currency       TEXT NOT NULL DEFAULT 'GHS' CHECK (currency = 'GHS'),
  status         TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'paid', 'failed', 'abandoned')),
  channel        TEXT,                                   -- mobile_money or card, from Paystack
  provider_id    BIGINT,                                 -- Paystack's transaction id
  created_by     TEXT NOT NULL REFERENCES users (id),    -- the health worker who started the checkout
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  paid_at        TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS payments_appointment_idx ON payments (appointment_id, created_at DESC);
CREATE INDEX IF NOT EXISTS payments_district_idx ON payments (district, created_at DESC);
-- A consultation is paid once: a second successful payment for the same visit is refused by the database.
CREATE UNIQUE INDEX IF NOT EXISTS payments_one_paid_per_visit ON payments (appointment_id) WHERE status = 'paid';

REVOKE ALL ON payments FROM hc_chv, hc_clinician, hc_ghs_official, hc_auditor;
