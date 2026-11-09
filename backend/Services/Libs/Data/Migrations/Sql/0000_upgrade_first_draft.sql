-- Migration 0: bring a database made from Liza's FIRST draft of the schema (the Lab 6 starter: patients, observations,
-- access_log, with no district, no FHIR ids, no coded vital signs) up to the shape migration 1 expects.
--
-- Why it is needed: migration 1 is the extended schema. Its "CREATE TABLE IF NOT EXISTS patients" does nothing when the first
-- draft's table is already there, and the statements after it (indexes, row-level security) need columns the first draft lacks.
-- Run on such a database, migration 1 stops with: column "district" does not exist.
--
-- What it does: only when patients exists WITHOUT the district column. On a new database, or one that already has the extended
-- schema, it does nothing at all. Nothing is dropped and no row is lost (the old nhis_number column stays; a later migration can
-- move it into patient_identifiers). It only adds what is missing and tidies the old values.
DO $$
BEGIN
  IF to_regclass('public.patients') IS NULL
     OR EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'patients' AND column_name = 'district') THEN
    RETURN;
  END IF;

  -- patients: the columns the later statements and the row-level security rely on.
  ALTER TABLE patients
    ADD COLUMN fhir_id              UUID    NOT NULL DEFAULT gen_random_uuid(),   -- one new id per existing patient
    ADD COLUMN birth_date_estimated BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN district             TEXT    NOT NULL DEFAULT coalesce(nullif(current_setting('app.district', true), ''), 'unassigned'),
                                                                                   -- old rows become 'unassigned': a clinician sees them, a CHV does not
    ADD COLUMN updated_at           TIMESTAMPTZ NOT NULL DEFAULT now();
  CREATE UNIQUE INDEX IF NOT EXISTS patients_fhir_id_key ON patients (fhir_id);

  -- The first draft stored the language as a word ('Twi'); the schema now stores the code ('tw').
  UPDATE patients SET preferred_language = CASE lower(preferred_language)
      WHEN 'english' THEN 'en' WHEN 'twi' THEN 'tw' WHEN 'ewe' THEN 'ee' WHEN 'dagbani' THEN 'dag' WHEN 'hausa' THEN 'ha'
      ELSE preferred_language END
    WHERE preferred_language IS NOT NULL;

  -- The rules the extended schema has on patients. NOT VALID checks every new or changed row and leaves old rows alone.
  ALTER TABLE patients
    ADD CONSTRAINT patients_name_not_blank   CHECK (length(trim(full_name)) > 0) NOT VALID,
    ADD CONSTRAINT patients_phone_format     CHECK (phone ~ '^\+233[0-9]{9}$') NOT VALID,
    ADD CONSTRAINT patients_birth_not_future CHECK (date_of_birth <= current_date) NOT VALID,
    ADD CONSTRAINT patients_gender_known     CHECK (gender IN ('male', 'female', 'other', 'unknown')) NOT VALID,
    ADD CONSTRAINT patients_language_known   CHECK (preferred_language IN ('en', 'tw', 'ee', 'dag', 'ha')) NOT VALID;

  -- observations: the columns the coded-vitals check, the BP panels and the district summary use.
  IF to_regclass('public.observations') IS NOT NULL THEN
    ALTER TABLE observations
      ADD COLUMN IF NOT EXISTS category TEXT NOT NULL DEFAULT 'vital-signs',
      ADD COLUMN IF NOT EXISTS panel_id UUID,
      ADD COLUMN IF NOT EXISTS value_quantity NUMERIC GENERATED ALWAYS AS (
        CASE WHEN value ~ '^-?[0-9]+(\.[0-9]+)?$' THEN value::numeric END) STORED;
  END IF;

  -- access_log: migration 1 already adds the columns it needs there (it was written for the first draft's table).
END $$;
