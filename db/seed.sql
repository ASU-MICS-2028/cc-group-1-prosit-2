-- HealthConnect Ghana: synthetic seed data (Lab 6, Part C)
-- Run after schema.sql and rbac.sql. SYNTHETIC ONLY: invented names ("Synthetic Patient ..."),
-- placeholder phone numbers (+2332000000xx). Based on Liza Aikins' first seed.
-- Districts match the brief's partner sites: Dodowa (Shai-Osudoku), Kintampo (Kintampo North)
-- and the Northern Region CHV network (Tamale Metropolitan).

BEGIN;

INSERT INTO patients
  (full_name, phone, date_of_birth, birth_date_estimated, gender, region, district,
   preferred_language, consent_given_at, consent_method)
VALUES
  ('Synthetic Patient A', '+233200000011', '1987-03-14', false, 'female', 'Greater Accra', 'Shai-Osudoku',       'tw',  now() - interval '9 days', 'spoken'),
  ('Synthetic Patient B', NULL,            '1959-07-01', true,  'male',   'Greater Accra', 'Shai-Osudoku',       'tw',  now() - interval '9 days', 'thumbprint'),
  ('Synthetic Patient C', '+233200000013', '2001-07-21', false, 'female', 'Greater Accra', 'Shai-Osudoku',       'ee',  now() - interval '8 days', 'signature'),
  ('Synthetic Patient D', NULL,            '1972-07-01', true,  'female', 'Greater Accra', 'Shai-Osudoku',       'tw',  now() - interval '8 days', 'thumbprint'),
  ('Synthetic Patient E', '+233200000015', '1995-01-30', false, 'male',   'Greater Accra', 'Shai-Osudoku',       'en',  now() - interval '7 days', 'spoken'),
  ('Synthetic Patient F', '+233200000016', '1964-09-12', false, 'female', 'Greater Accra', 'Shai-Osudoku',       'tw',  now() - interval '7 days', 'signature'),
  ('Synthetic Patient G', NULL,            '1980-07-01', true,  'male',   'Bono East',     'Kintampo North',     'tw',  now() - interval '6 days', 'thumbprint'),
  ('Synthetic Patient H', '+233200000018', '1999-05-05', false, 'female', 'Bono East',     'Kintampo North',     'tw',  now() - interval '5 days', 'spoken'),
  ('Synthetic Patient I', '+233200000019', '1990-11-23', false, 'female', 'Northern',      'Tamale Metropolitan', 'dag', now() - interval '4 days', 'spoken'),
  ('Synthetic Patient J', NULL,            '1955-07-01', true,  'male',   'Northern',      'Tamale Metropolitan', 'ha',  now() - interval '3 days', 'thumbprint');

-- Every patient gets our own record number, in our own namespace.
INSERT INTO patient_identifiers (patient_id, system, value)
SELECT id, 'urn:healthconnect-gh:mrn', 'HC-' || lpad(id::text, 6, '0') FROM patients;

-- Consent history mirrors the current state on the patient row.
INSERT INTO consents (patient_id, status, method, obtained_by, recorded_at)
SELECT id, 'active', consent_method, 'chv-042', consent_given_at FROM patients;

-- Vital signs: one row per value with its LOINC code and UCUM unit; BP pairs share a panel.
WITH v(full_name, days_ago, sys, dia, temp, pulse, weight) AS (VALUES
  ('Synthetic Patient A', 6, 118, 76, 36.8, 72,  64.5),
  ('Synthetic Patient B', 6, 152, 96, 36.9, 88,  71.0),
  ('Synthetic Patient C', 5, 112, 70, 38.6, 104, 58.2),
  ('Synthetic Patient D', 5, 186, 104, 37.1, 92, 80.4),
  ('Synthetic Patient E', 4, 124, 82, 36.6, 68,  77.9),
  ('Synthetic Patient F', 4, 146, 92, 38.2, 96,  69.3),
  ('Synthetic Patient G', 3, 134, 86, 39.1, 110, 66.0),
  ('Synthetic Patient H', 2, 108, 68, 36.7, 74,  55.4),
  ('Synthetic Patient I', 2, 121, 79, 38.4, 98,  61.7),
  ('Synthetic Patient J', 1, 165, 99, 36.5, 80,  74.8)
),
p AS (
  SELECT pt.id, v.*, gen_random_uuid() AS panel, now() - make_interval(days => v.days_ago) AS at
  FROM v JOIN patients pt ON pt.full_name = v.full_name
)
INSERT INTO observations
  (patient_id, observation_type, loinc_code, value, unit, panel_id, recorded_by, recorded_at)
SELECT id, 'systolic_bp',      '8480-6',  sys::text,    'mm[Hg]', panel, 'chv-042', at FROM p
UNION ALL
SELECT id, 'diastolic_bp',     '8462-4',  dia::text,    'mm[Hg]', panel, 'chv-042', at FROM p
UNION ALL
SELECT id, 'body_temperature', '8310-5',  temp::text,   'Cel',    NULL,  'chv-042', at FROM p
UNION ALL
SELECT id, 'heart_rate',       '8867-4',  pulse::text,  '/min',   NULL,  'chv-042', at FROM p
UNION ALL
SELECT id, 'body_weight',      '29463-7', weight::text, 'kg',     NULL,  'chv-042', at FROM p;

COMMIT;
