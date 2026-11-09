using HealthConnect.Data.Tests.Support;
using Npgsql;

namespace HealthConnect.Data.Tests;

/// <summary>Liza's sample data (db/seed.sql) must keep loading on top of the migrations: it is what the demo and the report use.</summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class SampleDataTests(SchemaFixture fixture)
{
    private static string SeedScript()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var file = Path.Combine(folder.FullName, "db", "seed.sql");
            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }
        }

        throw new FileNotFoundException("db/seed.sql was not found above the test folder.");
    }

    [Fact]
    public async Task The_sample_data_loads_into_a_migrated_database()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_sample_data");
        await SchemaFixture.MigrateAsync(connection);
        await using var source = NpgsqlDataSource.Create(connection);

        await using (var load = source.CreateCommand(SeedScript()))
        {
            await load.ExecuteNonQueryAsync();
        }

        await using var count = source.CreateCommand(
            "SELECT (SELECT count(*) FROM patients), (SELECT count(*) FROM consents), (SELECT count(*) FROM observations), (SELECT count(*) FROM patient_identifiers)");
        await using var reader = await count.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Equal([10L, 10L, 50L, 10L], new[] { reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3) });
    }

    [Fact]
    public async Task The_summary_of_the_sample_data_never_shows_a_number_under_five()
    {
        // The sample dates are relative to today, so which weeks reach 5 patients depends on the weekday: assert the rule, not the numbers.
        var connection = await fixture.CreateDatabaseAsync("hc_sample_summary");
        await SchemaFixture.MigrateAsync(connection);
        await using var source = NpgsqlDataSource.Create(connection);
        await using (var load = source.CreateCommand(SeedScript()))
        {
            await load.ExecuteNonQueryAsync();
        }

        await using var small = source.CreateCommand("SELECT count(*) FROM surveillance_weekly WHERE patients_seen < 5");
        await using var rows = source.CreateCommand("SELECT count(*) FROM surveillance_weekly");
        await using var hidden = source.CreateCommand("SELECT count(*) FROM surveillance_weekly WHERE suppressed AND patients_seen IS NOT NULL");

        Assert.True((long)(await rows.ExecuteScalarAsync())! > 0);
        Assert.Equal(0L, await small.ExecuteScalarAsync());   // a visible count is never under 5
        Assert.Equal(0L, await hidden.ExecuteScalarAsync());  // and a hidden one never leaks its number
    }
}
