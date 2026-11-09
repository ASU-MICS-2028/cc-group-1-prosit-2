using HealthConnect.Data.Persistence;
using HealthConnect.Data.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HealthConnect.Data.Tests;

/// <summary>The migrations themselves: they apply to an empty database, they can be run twice, and they cope with a database that already has Liza's schema.</summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class MigrationTests(SchemaFixture fixture)
{
    private static readonly string[] AllMigrations = ["20261109000000_UpgradeFirstDraft", "20261109000001_Baseline", "20261109000002_ClinicalRecords", "20261109000003_SignIn", "20261109000004_AuditEvents"];

    private static readonly string[] AllTables =
    [
        "patients", "patient_identifiers", "consents", "observation_codes", "observations", "access_log",
        "vitals", "appointments", "users", "login_codes", "media",
    ];

    private async Task<List<string>> TablesAsync(string connectionString)
    {
        await using var source = NpgsqlDataSource.Create(connectionString);
        await using var command = source.CreateCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY 1");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<string[]> AppliedAsync(string connectionString)
    {
        await using var db = SchemaFixture.NewContext(connectionString);
        return [.. await db.Database.GetAppliedMigrationsAsync()];
    }

    [Fact]
    public async Task Every_migration_applies_to_an_empty_database()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_fresh");

        await SchemaFixture.MigrateAsync(connection);

        Assert.Equal(AllMigrations, await AppliedAsync(connection));
        var tables = await TablesAsync(connection);
        foreach (var table in AllTables)
        {
            Assert.Contains(table, tables);
        }
    }

    [Fact]
    public async Task Running_the_migrations_again_changes_nothing()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_twice");
        await SchemaFixture.MigrateAsync(connection);

        await SchemaFixture.MigrateAsync(connection);

        await using var db = SchemaFixture.NewContext(connection);
        Assert.Equal(AllMigrations, await AppliedAsync(connection));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task All_migrations_are_pending_until_they_are_applied()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_pending");
        await using var db = SchemaFixture.NewContext(connection);

        Assert.Equal(AllMigrations, await db.Database.GetPendingMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task The_baseline_is_safe_on_a_database_where_the_schema_was_already_applied_by_hand()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_by_hand");
        var baseline = ReadScript("0001_baseline.sql");
        await using (var source = NpgsqlDataSource.Create(connection))
        {
            // What Liza did in Lab 6: run the two files directly (here twice, to also show the script can be repeated) ...
            await using var first = source.CreateCommand(baseline);
            await first.ExecuteNonQueryAsync();
            await using var second = source.CreateCommand(baseline);
            await second.ExecuteNonQueryAsync();
            // ... and the sample data that went in with it, including a consent with no proof recorded.
            await using var rows = source.CreateCommand(
                """
                INSERT INTO patients (full_name, district, consent_given_at, consent_method) VALUES ('Synthetic Old Patient', 'Shai-Osudoku', now(), 'spoken');
                INSERT INTO consents (patient_id, status, method, obtained_by) SELECT id, 'active', 'spoken', 'chv-042' FROM patients;
                """);
            await rows.ExecuteNonQueryAsync();
        }

        await SchemaFixture.MigrateAsync(connection);

        Assert.Equal(AllMigrations, await AppliedAsync(connection));
        await using var check = NpgsqlDataSource.Create(connection);
        await using var count = check.CreateCommand("SELECT count(*) FROM consents");
        Assert.Equal(1L, await count.ExecuteScalarAsync()); // the old consent is still there, untouched
    }

    [Fact]
    public async Task Migrations_only_go_forward()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_forward");
        await SchemaFixture.MigrateAsync(connection);
        await using var db = SchemaFixture.NewContext(connection);
        var migrator = db.GetService<IMigrator>();

        // asking to go back to before the baseline must refuse, never drop tables
        await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync("0"));
        Assert.Contains("patients", await TablesAsync(connection));
    }

    [Fact]
    public void All_scripts_ship_inside_the_assembly()
    {
        Assert.NotEmpty(ReadScript("0000_upgrade_first_draft.sql"));
        Assert.NotEmpty(ReadScript("0001_baseline.sql"));
        Assert.NotEmpty(ReadScript("0002_clinical_records.sql"));
        Assert.NotEmpty(ReadScript("0003_sign_in.sql"));
    }

    private static string ReadScript(string fileName)
    {
        using var stream = typeof(AppDbContext).Assembly.GetManifestResourceStream($"HealthConnect.Data.Sql.{fileName}")
            ?? throw new InvalidOperationException($"{fileName} is not embedded in the Data assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
