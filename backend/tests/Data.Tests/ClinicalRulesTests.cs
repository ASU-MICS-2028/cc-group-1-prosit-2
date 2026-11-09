using HealthConnect.Data.Tests.Support;

namespace HealthConnect.Data.Tests;

/// <summary>
/// The data-quality rules the database enforces by itself, so a bug or a wrong app version cannot store nonsense:
/// plausible vital signs, consent with proof, valid appointments, small files only.
/// </summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class ClinicalRulesTests(SchemaFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ vital signs (observations)

    [Fact]
    public async Task A_normal_reading_is_accepted_and_its_number_is_kept_as_a_number()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        await db.AddReadingAsync(patient, "body_temperature", "36.8", Now);

        Assert.Equal(36.8m, await db.ScalarAsync<decimal>("SELECT value_quantity FROM observations WHERE patient_id = @p", ("p", patient)));
    }

    [Theory]
    [InlineData("systolic_bp", "400")]          // above 260
    [InlineData("systolic_bp", "10")]           // below 50
    [InlineData("diastolic_bp", "200")]
    [InlineData("body_temperature", "98.6")]    // Fahrenheit typed into a Celsius field
    [InlineData("heart_rate", "500")]
    [InlineData("body_weight", "0")]
    [InlineData("body_height", "3")]
    [InlineData("body_height", "tall")]         // not a number at all
    public async Task An_implausible_or_non_numeric_value_is_refused(string type, string value)
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        var refusal = await db.FailsAsync(
            "INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by) " +
            "SELECT @p, @t, loinc_code, @v, ucum_unit, 'chv-test' FROM observation_codes WHERE short_name = @t",
            ("p", patient), ("t", type), ("v", value));

        Assert.Equal(SqlState.RaisedByTrigger, refusal.SqlState);
        Assert.Contains("plausible range", refusal.MessageText);
    }

    [Fact]
    public async Task The_wrong_unit_or_a_made_up_code_is_refused()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        var unit = await db.FailsAsync(
            "INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by) VALUES (@p, 'body_temperature', '8310-5', '36.8', 'degF', 'x')", ("p", patient));
        var code = await db.FailsAsync(
            "INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by) VALUES (@p, 'bp', 'free-text', '120', 'mm[Hg]', 'x')", ("p", patient));
        var mismatch = await db.FailsAsync(
            "INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by) VALUES (@p, 'heart_rate', '8310-5', '36.8', 'Cel', 'x')", ("p", patient));

        Assert.Contains("UCUM unit", unit.MessageText);
        Assert.Equal(SqlState.RaisedByTrigger, code.SqlState);
        Assert.Contains("free-text codes are not allowed", code.MessageText);
        Assert.Contains("is body_temperature, not heart_rate", mismatch.MessageText);
    }

    // ------------------------------------------------------------------ patients

    [Fact]
    public async Task Consent_time_and_method_are_recorded_together_or_not_at_all()
    {
        await using var db = await fixture.StartAsync();

        var timeOnly = await db.FailsAsync("INSERT INTO patients (full_name, district, consent_given_at) VALUES ('Synthetic', 'Shai-Osudoku', now())");
        var methodOnly = await db.FailsAsync("INSERT INTO patients (full_name, district, consent_method) VALUES ('Synthetic', 'Shai-Osudoku', 'spoken')");

        Assert.Contains("consent_recorded_together", timeOnly.ConstraintName);
        Assert.Contains("consent_recorded_together", methodOnly.ConstraintName);
    }

    [Theory]
    [InlineData("0241234567")]        // not in international form
    [InlineData("+233 24 123 4567")]  // spaces
    [InlineData("+23324123456")]      // too short
    public async Task A_phone_that_is_not_a_Ghana_number_in_international_form_is_refused(string phone)
    {
        await using var db = await fixture.StartAsync();

        var refusal = await db.FailsAsync("INSERT INTO patients (full_name, district, phone) VALUES ('Synthetic', 'Shai-Osudoku', @p)", ("p", phone));

        Assert.Equal(SqlState.CheckViolation, refusal.SqlState);
    }

    [Fact]
    public async Task A_birth_date_in_the_future_or_an_unknown_language_or_sex_is_refused()
    {
        await using var db = await fixture.StartAsync();

        var birth = await db.FailsAsync("INSERT INTO patients (full_name, district, date_of_birth) VALUES ('S', 'D', current_date + 1)");
        var language = await db.FailsAsync("INSERT INTO patients (full_name, district, preferred_language) VALUES ('S', 'D', 'fr')");
        var sex = await db.FailsAsync("INSERT INTO patients (full_name, district, gender) VALUES ('S', 'D', 'robot')");

        Assert.All([birth, language, sex], refusal => Assert.Equal(SqlState.CheckViolation, refusal.SqlState));
    }

    [Fact]
    public async Task The_phones_patient_id_is_unique_and_a_blank_name_or_zero_version_is_refused()
    {
        await using var db = await fixture.StartAsync();
        var id = Guid.NewGuid();
        await db.ExecAsync("INSERT INTO patients (fhir_id, full_name, district) VALUES (@id, 'Synthetic', 'D')", ("id", id));

        var again = await db.FailsAsync("INSERT INTO patients (fhir_id, full_name, district) VALUES (@id, 'Synthetic 2', 'D')", ("id", id));
        var blank = await db.FailsAsync("INSERT INTO patients (full_name, district) VALUES ('   ', 'D')");
        var version = await db.FailsAsync("INSERT INTO patients (full_name, district, version) VALUES ('S', 'D', 0)");
        var givenName = await db.FailsAsync("INSERT INTO patients (full_name, district, given_name) VALUES ('S', 'D', ' ')");

        Assert.Equal(SqlState.UniqueViolation, again.SqlState);
        Assert.All([blank, version, givenName], refusal => Assert.Equal(SqlState.CheckViolation, refusal.SqlState));
    }

    [Fact]
    public async Task The_new_patient_fields_are_stored_and_a_new_patient_starts_at_version_one()
    {
        await using var db = await fixture.StartAsync();

        var row = (await db.RowsAsync(
            "INSERT INTO patients (full_name, given_name, family_name, community, district, created_by) " +
            "VALUES ('Ama Test', 'Ama', 'Test', 'Dodowa', 'Shai-Osudoku', 'chv-042') RETURNING version, given_name, family_name, community, created_by")).Single();

        Assert.Equal([1, "Ama", "Test", "Dodowa", "chv-042"], row);
    }

    [Fact]
    public async Task One_identifier_belongs_to_one_patient()
    {
        await using var db = await fixture.StartAsync();
        var first = await db.AddPatientAsync();
        var second = await db.AddPatientAsync();
        await db.ExecAsync("INSERT INTO patient_identifiers (patient_id, system, value) VALUES (@p, 'urn:ghana-card', 'GHA-000000000-0')", ("p", first));

        var refusal = await db.FailsAsync("INSERT INTO patient_identifiers (patient_id, system, value) VALUES (@p, 'urn:ghana-card', 'GHA-000000000-0')", ("p", second));

        Assert.Equal(SqlState.UniqueViolation, refusal.SqlState);
    }

    // ------------------------------------------------------------------ consent proof

    [Fact]
    public async Task A_new_consent_needs_a_recording_or_a_witness()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        var neither = await db.FailsAsync("INSERT INTO consents (patient_id, status, method, obtained_by) VALUES (@p, 'active', 'spoken', 'chv-1')", ("p", patient));
        await db.ExecAsync("INSERT INTO consents (patient_id, status, method, obtained_by, witness, statement_version, language) VALUES (@p, 'active', 'spoken', 'chv-1', 'Synthetic Witness', 'v1', 'tw')", ("p", patient));
        await db.ExecAsync(
            "INSERT INTO consents (patient_id, status, method, obtained_by, evidence_sha256, evidence_mime, evidence_key) VALUES (@p, 'active', 'thumbprint', 'chv-1', @sha, 'image/jpeg', 'media/x/y')",
            ("p", patient), ("sha", new string('b', 64)));

        Assert.Equal("consents_has_evidence", neither.ConstraintName);
        Assert.Equal(2, await db.CountAsync("SELECT count(*) FROM consents WHERE patient_id = @p", ("p", patient)));
    }

    [Fact]
    public async Task Consent_proof_must_be_a_real_sha256_and_a_known_language_and_method()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        const string Base = "INSERT INTO consents (patient_id, status, method, obtained_by, witness, {0}) VALUES (@p, 'active', {1}, 'c', 'w', {2})";

        var hash = await db.FailsAsync(string.Format(Base, "evidence_sha256", "'spoken'", "'not-a-hash'"), ("p", patient));
        var upper = await db.FailsAsync(string.Format(Base, "evidence_sha256", "'spoken'", $"'{new string('A', 64)}'"), ("p", patient));
        var language = await db.FailsAsync(string.Format(Base, "language", "'spoken'", "'fr'"), ("p", patient));
        var method = await db.FailsAsync(string.Format(Base, "language", "'verbal'", "'en'"), ("p", patient)); // the app now sends "spoken"

        Assert.All([hash, upper, language, method], refusal => Assert.Equal(SqlState.CheckViolation, refusal.SqlState));
    }

    // ------------------------------------------------------------------ vitals

    [Fact]
    public async Task A_reading_has_at_most_one_value_per_measurement()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        var vitals = await db.AddVitalsAsync(patient);
        const string Sql =
            "INSERT INTO observations (patient_id, vitals_id, observation_type, loinc_code, value, unit, recorded_by) VALUES (@p, @v, @t, @c, @val, @u, 'x')";
        await db.ExecAsync(Sql, ("p", patient), ("v", vitals), ("t", "systolic_bp"), ("c", "8480-6"), ("val", "120"), ("u", "mm[Hg]"));
        await db.ExecAsync(Sql, ("p", patient), ("v", vitals), ("t", "diastolic_bp"), ("c", "8462-4"), ("val", "80"), ("u", "mm[Hg]"));

        var twice = await db.FailsAsync(Sql, ("p", patient), ("v", vitals), ("t", "systolic_bp"), ("c", "8480-6"), ("val", "125"), ("u", "mm[Hg]"));

        Assert.Equal(SqlState.UniqueViolation, twice.SqlState);
        Assert.Equal(2, await db.CountAsync("SELECT count(*) FROM observations WHERE vitals_id = @v", ("v", vitals)));
    }

    [Fact]
    public async Task A_reading_needs_a_patient_and_the_phones_id_is_unique()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        var id = await db.AddVitalsAsync(patient);

        var nobody = await db.FailsAsync("INSERT INTO vitals (id, patient_id, taken_at, recorded_by) VALUES (gen_random_uuid(), 999999, now(), 'x')");
        var again = await db.FailsAsync("INSERT INTO vitals (id, patient_id, taken_at, recorded_by) VALUES (@id, @p, now(), 'x')", ("id", id), ("p", patient));

        Assert.Equal(SqlState.ForeignKeyViolation, nobody.SqlState);
        Assert.Equal(SqlState.UniqueViolation, again.SqlState);
    }

    // ------------------------------------------------------------------ appointments

    [Fact]
    public async Task A_visit_a_referral_and_a_teleconsult_are_accepted_with_sensible_defaults()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        var visit = await db.AddAppointmentAsync(patient);
        await db.AddAppointmentAsync(patient, "referral", "requested");
        await db.AddAppointmentAsync(patient, "teleconsult", "requested");

        var row = (await db.RowsAsync("SELECT priority, status, version, referral_status FROM appointments WHERE id = @id", ("id", visit))).Single();
        Assert.Equal(["routine", "scheduled", 1, null], row);
    }

    [Theory]
    [InlineData("type", "surgery")]
    [InlineData("priority", "whenever")]
    [InlineData("status", "pending")]
    [InlineData("facility", "  ")]
    public async Task An_appointment_with_an_unknown_value_is_refused(string column, string value)
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        var columns = new Dictionary<string, string> { ["type"] = "'follow_up'", ["priority"] = "'routine'", ["status"] = "'scheduled'", ["facility"] = "'Clinic'" };
        columns[column] = $"'{value}'";

        var refusal = await db.FailsAsync(
            $"INSERT INTO appointments (id, patient_id, scheduled_at, type, priority, status, facility) VALUES (gen_random_uuid(), @p, now(), {columns["type"]}, {columns["priority"]}, {columns["status"]}, {columns["facility"]})",
            ("p", patient));

        Assert.Equal(SqlState.CheckViolation, refusal.SqlState);
    }

    [Fact]
    public async Task Only_a_referral_or_a_teleconsult_has_a_referral_status()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        var refusal = await db.FailsAsync(
            "INSERT INTO appointments (id, patient_id, type, scheduled_at, facility, referral_status) VALUES (gen_random_uuid(), @p, 'follow_up', now(), 'Clinic', 'requested')", ("p", patient));
        var unknown = await db.FailsAsync(
            "INSERT INTO appointments (id, patient_id, type, scheduled_at, facility, referral_status) VALUES (gen_random_uuid(), @p, 'referral', now(), 'Clinic', 'ignored')", ("p", patient));

        Assert.Equal("appointments_referral_status_only_for_referrals", refusal.ConstraintName);
        Assert.Equal(SqlState.CheckViolation, unknown.SqlState);
    }

    [Fact]
    public async Task Who_handled_a_referral_and_when_are_recorded_together()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        var referral = await db.AddAppointmentAsync(patient, "referral", "requested");

        var byOnly = await db.FailsAsync("UPDATE appointments SET handled_by = 'nurse-17' WHERE id = @id", ("id", referral));
        await db.ExecAsync("UPDATE appointments SET referral_status = 'accepted', handled_by = 'nurse-17', handled_at = now() WHERE id = @id", ("id", referral));

        Assert.Equal("appointments_handled_together", byOnly.ConstraintName);
        Assert.Equal("accepted", await db.ScalarAsync<string>("SELECT referral_status FROM appointments WHERE id = @id", ("id", referral)));
    }

    // ------------------------------------------------------------------ media

    [Fact]
    public async Task A_small_recording_or_photo_is_recorded()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        await db.AddMediaAsync(patient);

        Assert.Equal(1, await db.CountAsync("SELECT count(*) FROM media"));
    }

    [Theory]
    [InlineData(2_097_153, "audio/webm")]       // one byte over 2 MB
    [InlineData(0, "audio/webm")]
    [InlineData(1000, "application/pdf")]       // only audio, PNG and JPEG
    [InlineData(1000, "image/gif")]
    [InlineData(1000, "text/html")]
    public async Task A_file_that_is_too_big_or_the_wrong_type_is_refused(int size, string mime)
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        var refusal = await db.FailsAsync(
            "INSERT INTO media (id, patient_id, owner_entity, owner_id, kind, mime, size_bytes, sha256, s3_key, requested_by) " +
            "VALUES (gen_random_uuid(), @p, 'patient', gen_random_uuid(), 'wound-photo', @mime, @size, @sha, 'media/k/1', 'x')",
            ("p", patient), ("mime", mime), ("size", size), ("sha", new string('c', 64)));

        Assert.Equal(SqlState.CheckViolation, refusal.SqlState);
    }

    [Theory]
    [InlineData(2_097_152, "image/png")]        // exactly 2 MB is allowed
    [InlineData(1, "image/jpeg")]
    [InlineData(5000, "audio/mp4")]
    public async Task The_limits_themselves_are_allowed(int size, string mime)
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();

        await db.ExecAsync(
            "INSERT INTO media (id, patient_id, owner_entity, owner_id, kind, mime, size_bytes, sha256, s3_key, requested_by) " +
            "VALUES (gen_random_uuid(), @p, 'patient', gen_random_uuid(), 'consent-photo', @mime, @size, @sha, 'media/k/1', 'x')",
            ("p", patient), ("mime", mime), ("size", size), ("sha", new string('c', 64)));

        Assert.Equal(1, await db.CountAsync("SELECT count(*) FROM media"));
    }

    [Fact]
    public async Task A_file_needs_a_real_hash_a_known_kind_and_its_own_storage_key()
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        await db.AddMediaAsync(patient, "media/same/key");
        const string Sql =
            "INSERT INTO media (id, patient_id, owner_entity, owner_id, kind, mime, size_bytes, sha256, s3_key, requested_by) " +
            "VALUES (gen_random_uuid(), @p, 'patient', gen_random_uuid(), @kind, 'audio/webm', 10, @sha, @key, 'x')";

        var hash = await db.FailsAsync(Sql, ("p", patient), ("kind", "wound-photo"), ("sha", "abc"), ("key", "media/a"));
        var kind = await db.FailsAsync(Sql, ("p", patient), ("kind", "selfie"), ("sha", new string('d', 64)), ("key", "media/b"));
        var key = await db.FailsAsync(Sql, ("p", patient), ("kind", "wound-photo"), ("sha", new string('d', 64)), ("key", "media/same/key"));

        Assert.Equal(SqlState.CheckViolation, hash.SqlState);
        Assert.Equal(SqlState.CheckViolation, kind.SqlState);
        Assert.Equal(SqlState.UniqueViolation, key.SqlState);
    }

    // ------------------------------------------------------------------ users and sign-in codes

    [Fact]
    public async Task A_health_worker_is_stored_with_a_known_role_and_a_Ghana_number()
    {
        await using var db = await fixture.StartAsync();

        await db.ExecAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('chv-042', '+233200000101', 'Akosua Mensah', 'chv', 'Shai-Osudoku')");

        Assert.True(await db.ScalarAsync<bool>("SELECT active FROM users WHERE id = 'chv-042'"));
    }

    [Theory]
    [InlineData("+233200000101", "nurse-2", "superuser")]   // not a role
    [InlineData("020 000 0101", "nurse-3", "nurse")]        // not international form
    [InlineData("+233200000102", "", "nurse")]              // empty id
    public async Task A_bad_health_worker_is_refused(string phone, string id, string role)
    {
        await using var db = await fixture.StartAsync();

        var refusal = await db.FailsAsync(
            "INSERT INTO users (id, phone, name, role, district) VALUES (@id, @phone, 'Synthetic', @role, 'Shai-Osudoku')",
            ("id", id), ("phone", phone), ("role", role));

        Assert.Equal(SqlState.CheckViolation, refusal.SqlState);
    }

    [Fact]
    public async Task A_phone_number_belongs_to_one_health_worker()
    {
        await using var db = await fixture.StartAsync();
        await db.ExecAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('a', '+233200000101', 'A', 'chv', 'D')");

        var refusal = await db.FailsAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('b', '+233200000101', 'B', 'nurse', 'D')");

        Assert.Equal(SqlState.UniqueViolation, refusal.SqlState);
    }

    [Fact]
    public async Task A_sign_in_code_is_stored_hashed_and_must_expire_after_it_was_made()
    {
        await using var db = await fixture.StartAsync();
        var hash = new string('f', 64);

        await db.ExecAsync("INSERT INTO login_codes (phone, code_hash, expires_at) VALUES ('+233200000101', @h, now() + interval '10 minutes')", ("h", hash));
        var past = await db.FailsAsync("INSERT INTO login_codes (phone, code_hash, expires_at) VALUES ('+233200000101', @h, now() - interval '1 minute')", ("h", hash));
        var plain = await db.FailsAsync("INSERT INTO login_codes (phone, code_hash, expires_at) VALUES ('+233200000101', '123456', now() + interval '10 minutes')");

        Assert.Equal("login_codes_expires_after_created", past.ConstraintName);
        Assert.Equal(SqlState.CheckViolation, plain.SqlState); // a 6-digit code cannot be stored as it is
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT attempts FROM login_codes"));
    }
}
