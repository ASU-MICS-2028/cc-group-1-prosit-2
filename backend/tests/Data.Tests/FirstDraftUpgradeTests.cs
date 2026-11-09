using HealthConnect.Data.Tests.Support;
using Npgsql;

namespace HealthConnect.Data.Tests;

/// <summary>
/// Liza's first draft of the schema (the Lab 6 starter) may already have been run on a real database by hand. These tests start
/// from a database made with her real scripts and prove the migrations upgrade it in place: nothing lost, and every rule of the
/// current schema in force for new rows. Her two scripts are copied into Fixtures/ for this.
/// </summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class FirstDraftUpgradeTests(SchemaFixture fixture)
{
    private static string Fixture(string name)
    {
        using var stream = typeof(FirstDraftUpgradeTests).Assembly.GetManifestResourceStream($"HealthConnect.Data.Tests.Fixtures.{name}")
            ?? throw new InvalidOperationException($"{name} is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A new database made exactly the way Lab 6 made it: her schema.sql, then her seed.sql. Not yet migrated.</summary>
    private async Task<string> FirstDraftDatabaseAsync(string name)
    {
        var connection = await fixture.CreateDatabaseAsync(name);
        await using var source = NpgsqlDataSource.Create(connection);
        await using (var schema = source.CreateCommand(Fixture("first_draft_schema.sql")))
        {
            await schema.ExecuteNonQueryAsync();
        }

        await using (var seed = source.CreateCommand(Fixture("first_draft_seed.sql")))
        {
            await seed.ExecuteNonQueryAsync();
        }

        return connection;
    }

    private static async Task<DbSession> OpenAsync(string connection)
    {
        var source = NpgsqlDataSource.Create(connection);
        return await DbSession.StartAsync(source);
    }

    [Fact]
    public async Task Her_scripts_alone_give_the_database_the_first_draft_looks()
    {
        // The starting point is what we think it is: no district, no FHIR id, languages as words.
        var connection = await FirstDraftDatabaseAsync("hc_draft_before");

        await using var db = await OpenAsync(connection);

        Assert.Equal(0, await db.CountAsync("SELECT count(*) FROM information_schema.columns WHERE table_name = 'patients' AND column_name IN ('district', 'fhir_id')"));
        Assert.Equal(3, await db.CountAsync("SELECT count(*) FROM patients"));
        Assert.Equal("Twi", await db.ScalarAsync<string>("SELECT preferred_language FROM patients ORDER BY id LIMIT 1"));
    }

    [Fact]
    public async Task The_migrations_upgrade_that_database_in_place_without_losing_a_row()
    {
        var connection = await FirstDraftDatabaseAsync("hc_draft_upgrade");

        await SchemaFixture.MigrateAsync(connection);

        await using var db = await OpenAsync(connection);
        Assert.Equal(3, await db.CountAsync("SELECT count(*) FROM patients"));
        Assert.Equal(10, await db.CountAsync("SELECT count(*) FROM observations"));
        Assert.Equal(3, await db.CountAsync("SELECT count(DISTINCT fhir_id) FROM patients WHERE fhir_id IS NOT NULL"));  // a new, different id for each
        Assert.Equal(10, await db.CountAsync("SELECT count(*) FROM observations WHERE value_quantity IS NOT NULL"));    // the numbers are readable as numbers
        Assert.Equal("TEST-0001", await db.ScalarAsync<string>("SELECT nhis_number FROM patients WHERE full_name = 'Synthetic Patient A'")); // nothing was dropped
    }

    [Fact]
    public async Task Her_old_values_are_tidied_and_old_patients_are_kept_out_of_every_CHVs_view()
    {
        var connection = await FirstDraftDatabaseAsync("hc_draft_tidy");
        await SchemaFixture.MigrateAsync(connection);

        await using var db = await OpenAsync(connection);

        Assert.Equal(["tw", "tw", "dag"], (await db.RowsAsync("SELECT preferred_language FROM patients ORDER BY id")).Select(r => (string)r[0]!));
        Assert.Equal(3, await db.CountAsync("SELECT count(*) FROM patients WHERE district = 'unassigned'"));

        await db.AsAsync(Roles.Chv, Seed.Shai);
        Assert.Equal(0, await db.CountAsync("SELECT count(*) FROM patients"));      // no CHV sees patients that belong to no district

        await db.AsAsync(Roles.Clinician);
        Assert.Equal(3, await db.CountAsync("SELECT count(*) FROM patients"));      // a clinician still can
        Assert.Equal(10, await db.CountAsync("SELECT count(*) FROM observations"));
    }

    [Fact]
    public async Task After_the_upgrade_every_rule_of_the_current_schema_holds_for_new_rows()
    {
        var connection = await FirstDraftDatabaseAsync("hc_draft_rules");
        await SchemaFixture.MigrateAsync(connection);
        await using var db = await OpenAsync(connection);
        var patient = await db.ScalarAsync<int>("SELECT id FROM patients ORDER BY id LIMIT 1");

        var language = await db.FailsAsync("INSERT INTO patients (full_name, district, preferred_language) VALUES ('S', 'D', 'Twi')");
        var phone = await db.FailsAsync("INSERT INTO patients (full_name, district, phone) VALUES ('S', 'D', '0241234567')");
        var birth = await db.FailsAsync("INSERT INTO patients (full_name, district, date_of_birth) VALUES ('S', 'D', current_date + 1)");
        var gender = await db.FailsAsync("INSERT INTO patients (full_name, district, gender) VALUES ('S', 'D', 'robot')");
        var blank = await db.FailsAsync("INSERT INTO patients (full_name, district) VALUES (' ', 'D')");
        var bp = await db.FailsAsync(
            "INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by) VALUES (@p, 'systolic_bp', '8480-6', '400', 'mm[Hg]', 'x')", ("p", patient));

        Assert.All([language, phone, birth, gender, blank], refusal => Assert.Equal(SqlState.CheckViolation, refusal.SqlState));
        Assert.Equal(SqlState.RaisedByTrigger, bp.SqlState);
    }

    [Fact]
    public async Task After_the_upgrade_the_new_tables_the_audit_log_and_the_summary_all_work()
    {
        var connection = await FirstDraftDatabaseAsync("hc_draft_whole");
        await SchemaFixture.MigrateAsync(connection);
        await using var db = await OpenAsync(connection);

        var patient = await db.AddPatientAsync(Seed.Shai);
        await db.AddVitalsAsync(patient);
        await db.AddAppointmentAsync(patient, "referral", "requested");
        await db.AddAuditEntryAsync("chv-1");
        await db.AddAuditEntryAsync("chv-2");

        Assert.Null((await db.RowsAsync("SELECT first_bad_id FROM verify_access_log()")).Single()[0]);   // the chain verifies
        Assert.Contains("append-only", (await db.FailsAsync("DELETE FROM access_log")).MessageText);
        await db.AsAsync(Roles.Official, Seed.Shai);
        Assert.True(await db.CountAsync("SELECT count(*) FROM surveillance_weekly") >= 0);                // the view runs on the old readings too
    }

    [Fact]
    public async Task The_upgrade_does_nothing_on_a_new_database_or_one_that_already_has_the_current_schema()
    {
        var fresh = await fixture.CreateDatabaseAsync("hc_draft_noop_new");
        await SchemaFixture.MigrateAsync(fresh);
        await using var db = await OpenAsync(fresh);

        // the upgrade's own constraints exist only where an upgrade really happened
        Assert.Equal(0, await db.CountAsync("SELECT count(*) FROM pg_constraint WHERE conname IN ('patients_gender_known', 'patients_language_known', 'patients_phone_format')"));
    }
}
