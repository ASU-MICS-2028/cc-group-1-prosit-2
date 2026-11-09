-- A COPY of Liza Aikins' first-draft sample data, the test fixture that goes with first_draft_schema.sql (synthetic only).

-- HealthConnect Ghana: synthetic seed data (Lab 6, Part C)
-- Run after schema.sql. SYNTHETIC ONLY: invented names, placeholder phone
-- numbers (+23320000000x) and obviously fake NHIS numbers (TEST-...).

BEGIN;

INSERT INTO patients
  (full_name, phone, date_of_birth, gender, region, preferred_language,
   nhis_number, consent_given_at, consent_method)
VALUES
  ('Synthetic Patient A', '+233200000011', '1987-03-14', 'female', 'Greater Accra', 'Twi',
   'TEST-0001', now() - interval '3 days', 'spoken'),
  ('Synthetic Patient B', NULL,            '1959-11-02', 'male',   'Bono East',     'Twi',
   NULL,        now() - interval '2 days', 'thumbprint'),
  ('Synthetic Patient C', '+233200000013', '2001-07-21', 'female', 'Northern',      'Dagbani',
   NULL,        now() - interval '1 day',  'signature');

-- One row per measured value; blood pressure is two rows (systolic + diastolic).
INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by, recorded_at)
SELECT p.id, v.observation_type, v.loinc_code, v.value, v.unit, 'chv-042', now() - v.ago
FROM patients p
JOIN (VALUES
  ('Synthetic Patient A', 'systolic_bp',      '8480-6',  '118',  'mm[Hg]', interval '3 days'),
  ('Synthetic Patient A', 'diastolic_bp',     '8462-4',  '76',   'mm[Hg]', interval '3 days'),
  ('Synthetic Patient A', 'body_temperature', '8310-5',  '36.8', 'Cel',    interval '3 days'),
  ('Synthetic Patient A', 'body_weight',      '29463-7', '64.5', 'kg',     interval '3 days'),
  ('Synthetic Patient B', 'systolic_bp',      '8480-6',  '152',  'mm[Hg]', interval '2 days'),
  ('Synthetic Patient B', 'diastolic_bp',     '8462-4',  '96',   'mm[Hg]', interval '2 days'),
  ('Synthetic Patient B', 'heart_rate',       '8867-4',  '88',   '/min',   interval '2 days'),
  ('Synthetic Patient C', 'body_temperature', '8310-5',  '38.6', 'Cel',    interval '1 day'),
  ('Synthetic Patient C', 'body_height',      '8302-2',  '162',  'cm',     interval '1 day'),
  ('Synthetic Patient C', 'heart_rate',       '8867-4',  '104',  '/min',   interval '1 day')
) AS v(full_name, observation_type, loinc_code, value, unit, ago)
  ON p.full_name = v.full_name;

COMMIT;
