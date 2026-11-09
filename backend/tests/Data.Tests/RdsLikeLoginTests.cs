using HealthConnect.Data.Tests.Support;
using Npgsql;

namespace HealthConnect.Data.Tests;

/// <summary>
/// The same migrations and the key security rules, but as a database login that is NOT a superuser, the way the API's
/// "healthconnect" login on RDS is (RDS never hands out a true superuser). Everything else in this test project runs as
/// a superuser, which can hide permission problems: granting roles, creating the extension, bypassing row security.
/// It has its own database server, so the roles are created by this limited login, as they will be on RDS.
/// </summary>
[IntegrationTest]
public sealed class RdsLikeLoginTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private NpgsqlDataSource _login = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await using (var admin = NpgsqlDataSource.Create(_postgres.ConnectionString))
        {
            await using var role = admin.CreateCommand("CREATE ROLE rds_like LOGIN CREATEROLE PASSWORD 'rds-like-pw'");
            await role.ExecuteNonQueryAsync();
            await using var database = admin.CreateCommand("CREATE DATABASE hc_rds_like OWNER rds_like");
            await database.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString)
        {
            Username = "rds_like",
            Password = "rds-like-pw",
            Database = "hc_rds_like",
        }.ConnectionString;
        await SchemaFixture.MigrateAsync(connection);
        _login = NpgsqlDataSource.Create(connection);
    }

    public async Task DisposeAsync()
    {
        await _login.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task The_login_is_not_a_superuser_and_still_applied_every_migration()
    {
        await using var db = await DbSession.StartAsync(_login);

        Assert.Equal("off", await db.ScalarAsync<string>("SHOW is_superuser"));
        Assert.Equal(6L, await db.CountAsync("SELECT count(*) FROM \"__EFMigrationsHistory\""));
        Assert.Equal(4L, await db.CountAsync("SELECT count(*) FROM pg_roles WHERE rolname LIKE 'hc\\_%'"));
    }

    [Fact]
    public async Task It_can_act_as_each_of_the_four_roles()
    {
        await using var db = await DbSession.StartAsync(_login);

        foreach (var role in new[] { Roles.Chv, Roles.Clinician, Roles.Official, Roles.Auditor })
        {
            await db.AsAsync(role, Seed.Shai);
            Assert.Equal(role, await db.ScalarAsync<string>("SELECT current_user"));
            await db.AsOwnerAsync();
        }
    }

    [Fact]
    public async Task Row_level_security_still_holds_when_the_login_is_not_a_superuser()
    {
        await using var db = await DbSession.StartAsync(_login);
        await db.AddPatientAsync(Seed.Shai);
        await db.AddPatientAsync(Seed.Kintampo);

        await db.AsAsync(Roles.Chv, Seed.Shai);
        Assert.Equal(1, await db.CountAsync("SELECT count(*) FROM patients"));
        var refusal = await db.FailsAsync("INSERT INTO patients (full_name, district) VALUES ('Synthetic', @d)", ("d", Seed.Kintampo));
        Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);

        await db.AsAsync(Roles.Official, Seed.Shai);
        Assert.Equal(SqlState.InsufficientPrivilege, (await db.FailsAsync("SELECT * FROM patients")).SqlState);
    }

    [Fact]
    public async Task The_audit_log_is_still_append_only_and_checkable()
    {
        await using var db = await DbSession.StartAsync(_login);
        await db.AddAuditEntryAsync("chv-1");
        await db.AddAuditEntryAsync("chv-2");

        Assert.Contains("append-only", (await db.FailsAsync("UPDATE access_log SET accessed_by = 'x'")).MessageText);
        await db.AsAsync(Roles.Auditor, Seed.Shai);
        Assert.Null((await db.RowsAsync("SELECT first_bad_id FROM verify_access_log()")).Single()[0]);
    }

    [Fact]
    public async Task The_summary_works_for_an_officer_who_cannot_read_the_tables_behind_it()
    {
        await using var db = await DbSession.StartAsync(_login);
        var monday = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Shai, monday);
        }

        await db.AsAsync(Roles.Official, Seed.Shai);

        Assert.Equal(5L, await db.ScalarAsync<long>("SELECT patients_seen FROM surveillance_weekly WHERE district = @d", ("d", Seed.Shai)));
    }
}
