using HealthConnect.Data.Tests.Support;

namespace HealthConnect.Data.Tests;

/// <summary>
/// Who may see and change what. These run as the database roles the API switches into, so they prove Postgres itself refuses
/// the wrong person: even a bug in the API could not show a CHV another district's patients.
/// </summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class RowLevelSecurityTests(SchemaFixture fixture)
{
    [Fact]
    public async Task A_CHV_sees_only_the_patients_of_their_own_district()
    {
        await using var db = await fixture.StartAsync();
        await db.AddPatientAsync(Seed.Shai, "Synthetic A");
        await db.AddPatientAsync(Seed.Shai, "Synthetic B");
        await db.AddPatientAsync(Seed.Kintampo, "Synthetic C");

        await db.AsAsync(Roles.Chv, Seed.Shai);

        Assert.Equal(2, await db.CountAsync("SELECT count(*) FROM patients"));
        Assert.Equal(0, await db.CountAsync("SELECT count(*) FROM patients WHERE district = @d", ("d", Seed.Kintampo)));
    }

    [Fact]
    public async Task A_CHV_with_no_district_set_sees_nobody()
    {
        await using var db = await fixture.StartAsync();
        await db.AddPatientAsync(Seed.Shai);

        await db.AsAsync(Roles.Chv);

        Assert.Equal(0, await db.CountAsync("SELECT count(*) FROM patients"));
    }

    [Fact]
    public async Task A_CHV_cannot_register_a_patient_in_another_district()
    {
        await using var db = await fixture.StartAsync();
        await db.AsAsync(Roles.Chv, Seed.Shai);

        var refusal = await db.FailsAsync("INSERT INTO patients (full_name, district) VALUES ('Synthetic X', @d)", ("d", Seed.Kintampo));

        Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);
        Assert.Contains("row-level security", refusal.MessageText);
    }

    [Fact]
    public async Task A_CHV_registers_in_their_own_district_and_a_missing_district_defaults_to_it()
    {
        await using var db = await fixture.StartAsync();
        await db.AsAsync(Roles.Chv, Seed.Shai);

        var district = await db.ScalarAsync<string>("INSERT INTO patients (full_name) VALUES ('Synthetic Y') RETURNING district");

        Assert.Equal(Seed.Shai, district);
    }

    [Fact]
    public async Task A_CHV_cannot_move_a_patient_to_another_district()
    {
        await using var db = await fixture.StartAsync();
        var id = await db.AddPatientAsync(Seed.Shai);
        await db.AsAsync(Roles.Chv, Seed.Shai);

        var refusal = await db.FailsAsync("UPDATE patients SET district = @d WHERE id = @id", ("d", Seed.Kintampo), ("id", id));

        Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);
    }

    [Fact]
    public async Task A_clinician_sees_and_writes_in_every_district()
    {
        await using var db = await fixture.StartAsync();
        await db.AddPatientAsync(Seed.Shai);
        await db.AddPatientAsync(Seed.Kintampo);

        await db.AsAsync(Roles.Clinician, Seed.Shai);

        Assert.Equal(2, await db.CountAsync("SELECT count(*) FROM patients"));
        await db.ExecAsync("INSERT INTO patients (full_name, district) VALUES ('Synthetic Z', @d)", ("d", Seed.Kintampo));
        Assert.Equal(3, await db.CountAsync("SELECT count(*) FROM patients"));
    }

    [Theory]
    [InlineData(Roles.Official)]
    [InlineData(Roles.Auditor)]
    public async Task The_district_officer_and_the_auditor_cannot_read_patients_at_all(string role)
    {
        await using var db = await fixture.StartAsync();
        await db.AddPatientAsync();
        await db.AsAsync(role, Seed.Shai);

        foreach (var table in new[] { "patients", "observations", "consents", "patient_identifiers", "vitals", "appointments", "media" })
        {
            var refusal = await db.FailsAsync($"SELECT count(*) FROM {table}");
            Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);
            Assert.Contains("permission denied", refusal.MessageText);
        }
    }

    [Theory]
    [InlineData(Roles.Chv)]
    [InlineData(Roles.Clinician)]
    [InlineData(Roles.Official)]
    [InlineData(Roles.Auditor)]
    public async Task Nobody_can_read_the_sign_in_tables(string role)
    {
        await using var db = await fixture.StartAsync();
        await db.AsAsync(role, Seed.Shai);

        Assert.Equal(SqlState.InsufficientPrivilege, (await db.FailsAsync("SELECT * FROM users")).SqlState);
        Assert.Equal(SqlState.InsufficientPrivilege, (await db.FailsAsync("SELECT * FROM login_codes")).SqlState);
    }

    [Theory]
    [InlineData(Roles.Chv)]
    [InlineData(Roles.Clinician)]
    public async Task Clinical_data_is_never_deleted(string role)
    {
        await using var db = await fixture.StartAsync();
        var patient = await db.AddPatientAsync();
        await db.AddVitalsAsync(patient);
        await db.AddAppointmentAsync(patient);
        await db.AsAsync(role, Seed.Shai);

        foreach (var table in new[] { "patients", "observations", "consents", "vitals", "appointments", "media", "patient_identifiers" })
        {
            var refusal = await db.FailsAsync($"DELETE FROM {table}");
            Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);
        }
    }

    [Fact]
    public async Task Everything_attached_to_a_patient_follows_the_patients_district()
    {
        await using var db = await fixture.StartAsync();
        var here = await db.AddPatientAsync(Seed.Shai);
        var there = await db.AddPatientAsync(Seed.Kintampo);
        foreach (var patient in new[] { here, there })
        {
            await db.AddVitalsAsync(patient);
            await db.AddAppointmentAsync(patient);
            await db.AddMediaAsync(patient);
            await db.AddReadingAsync(patient, "systolic_bp", "120", DateTimeOffset.UtcNow);
            await db.ExecAsync("INSERT INTO consents (patient_id, status, method, obtained_by, witness) VALUES (@p, 'active', 'spoken', 'chv-test', 'Synthetic Witness')", ("p", patient));
            await db.ExecAsync("INSERT INTO patient_identifiers (patient_id, system, value) VALUES (@p, 'urn:test', @v)", ("p", patient), ("v", $"id-{patient}"));
        }

        await db.AsAsync(Roles.Chv, Seed.Shai);

        foreach (var table in new[] { "vitals", "appointments", "media", "observations", "consents", "patient_identifiers" })
        {
            Assert.Equal(1, await db.CountAsync($"SELECT count(*) FROM {table}"));
            Assert.Equal(1, await db.CountAsync($"SELECT count(*) FROM {table} WHERE patient_id = @p", ("p", here)));
        }

        await db.AsAsync(Roles.Clinician);
        foreach (var table in new[] { "vitals", "appointments", "media", "observations", "consents", "patient_identifiers" })
        {
            Assert.Equal(2, await db.CountAsync($"SELECT count(*) FROM {table}"));
        }
    }

    [Fact]
    public async Task A_CHV_cannot_attach_a_record_to_a_patient_in_another_district()
    {
        await using var db = await fixture.StartAsync();
        var there = await db.AddPatientAsync(Seed.Kintampo);
        await db.AsAsync(Roles.Chv, Seed.Shai);

        var vitals = await db.FailsAsync(
            "INSERT INTO vitals (id, patient_id, taken_at, recorded_by) VALUES (gen_random_uuid(), @p, now(), 'chv-test')", ("p", there));
        var appointment = await db.FailsAsync(
            "INSERT INTO appointments (id, patient_id, type, scheduled_at, facility) VALUES (gen_random_uuid(), @p, 'follow_up', now(), 'Clinic')", ("p", there));

        Assert.Equal(SqlState.InsufficientPrivilege, vitals.SqlState);
        Assert.Equal(SqlState.InsufficientPrivilege, appointment.SqlState);
    }
}
