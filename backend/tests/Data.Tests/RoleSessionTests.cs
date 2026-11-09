using HealthConnect.Data.Tests.Support;
using HealthConnect.SharedLibrary.Security;
using Npgsql;

namespace HealthConnect.Data.Tests;

/// <summary>The request-scoped session that makes Postgres act as the signed-in worker's database role in the worker's district.</summary>
[UnitTest]
public sealed class DbRolesTests
{
    [Theory]
    [InlineData("chv", "hc_chv")]
    [InlineData("nurse", "hc_clinician")]
    [InlineData("doctor", "hc_clinician")]
    [InlineData("district_officer", "hc_ghs_official")]
    [InlineData("admin", "hc_auditor")]
    public void Each_app_role_acts_as_its_database_role(string appRole, string dbRole) =>
        Assert.Equal(dbRole, DbRoles.For(appRole));

    [Theory]
    [InlineData("")]
    [InlineData("superuser")]
    [InlineData("CHV")]
    [InlineData("chv; DROP TABLE patients")]
    public void An_unknown_role_is_refused_and_never_turned_into_a_default(string role) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DbRoles.For(role));

    [Fact]
    public void Every_role_the_app_knows_has_a_database_role() =>
        Assert.All(HealthConnect.SharedLibrary.Security.Roles.All, role => Assert.StartsWith("hc_", DbRoles.For(role)));
}

[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class RoleSessionTests(SchemaFixture fixture)
{
    private static CurrentUser Worker(string role, string district = Seed.Shai) => new($"{role}-1", "Synthetic", role, district);

    private async Task<long> CountAsync(RoleSession session)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM patients WHERE full_name LIKE 'Synthetic Session %'", session.Connection, session.Transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task The_session_acts_as_the_workers_database_role_and_district()
    {
        await using var session = await RoleSession.BeginAsync(fixture.Admin, Worker("chv"), CancellationToken.None);

        await using var who = new NpgsqlCommand("SELECT current_user, current_setting('app.district'), current_setting('app.user_id')", session.Connection, session.Transaction);
        await using var reader = await who.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal(["hc_chv", Seed.Shai, "chv-1"], new[] { reader.GetString(0), reader.GetString(1), reader.GetString(2) });
    }

    [Fact]
    public async Task What_the_worker_saves_is_kept_only_after_commit()
    {
        await using (var undone = await RoleSession.BeginAsync(fixture.Admin, Worker("nurse"), CancellationToken.None))
        {
            await using var insert = new NpgsqlCommand("INSERT INTO patients (full_name, district) VALUES ('Synthetic Commit undone', 'Shai-Osudoku')", undone.Connection, undone.Transaction);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var kept = await RoleSession.BeginAsync(fixture.Admin, Worker("nurse"), CancellationToken.None))
        {
            await using var insert = new NpgsqlCommand("INSERT INTO patients (full_name, district) VALUES ('Synthetic Commit kept', 'Shai-Osudoku')", kept.Connection, kept.Transaction);
            await insert.ExecuteNonQueryAsync();
            await kept.CommitAsync(CancellationToken.None);
        }

        await using var check = await RoleSession.BeginAsync(fixture.Admin, Worker("nurse"), CancellationToken.None);
        await using var names = new NpgsqlCommand("SELECT full_name FROM patients WHERE full_name LIKE 'Synthetic Commit %'", check.Connection, check.Transaction);
        var found = new List<string>();
        await using var reader = await names.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        Assert.Equal(["Synthetic Commit kept"], found);
    }

    [Fact]
    public async Task A_CHV_session_only_sees_its_own_district()
    {
        await using (var seed = await RoleSession.BeginAsync(fixture.Admin, Worker("nurse"), CancellationToken.None))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO patients (full_name, district) VALUES ('Synthetic Session here', 'Shai-Osudoku'), ('Synthetic Session there', 'Kintampo North')", seed.Connection, seed.Transaction);
            await insert.ExecuteNonQueryAsync();
            await seed.CommitAsync(CancellationToken.None);
        }

        await using var chv = await RoleSession.BeginAsync(fixture.Admin, Worker("chv"), CancellationToken.None);
        await using var nurse = await RoleSession.BeginAsync(fixture.Admin, Worker("nurse"), CancellationToken.None);

        Assert.Equal(1, await CountAsync(chv));
        Assert.Equal(2, await CountAsync(nurse));
    }

    [Fact]
    public async Task The_connection_goes_back_to_the_pool_as_the_owner_not_as_a_worker()
    {
        await using (var session = await RoleSession.BeginAsync(fixture.Admin, Worker("chv"), CancellationToken.None))
        {
            await session.CommitAsync(CancellationToken.None);
        }

        await using var plain = await fixture.Admin.OpenConnectionAsync();
        await using var who = new NpgsqlCommand("SELECT current_user", plain);
        Assert.NotEqual("hc_chv", (string)(await who.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_session_for_an_unknown_role_never_opens()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await RoleSession.BeginAsync(fixture.Admin, Worker("superuser"), CancellationToken.None));
    }

    [Fact]
    public async Task Only_a_worker_who_exists_and_is_switched_on_gets_a_session()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using (var persist = source.CreateCommand(
            "INSERT INTO users (id, phone, name, role, district, active) VALUES ('on-1', '+233200009001', 'On', 'chv', 'Shai-Osudoku', true), ('off-1', '+233200009002', 'Off', 'chv', 'Shai-Osudoku', false) ON CONFLICT DO NOTHING"))
        {
            await persist.ExecuteNonQueryAsync();
        }

        var sessions = new RoleSessions(new Database(
            new DatabaseSettings(fixture.ConnectionString, null, 5432, null, "x", null, null, null),
            NSubstitute.Substitute.For<IDatabaseSecretReader>()));

        await using var on = await sessions.TryBeginAsync(new CurrentUser("on-1", "On", "chv", Seed.Shai), CancellationToken.None);
        var off = await sessions.TryBeginAsync(new CurrentUser("off-1", "Off", "chv", Seed.Shai), CancellationToken.None);
        var ghost = await sessions.TryBeginAsync(new CurrentUser("ghost-1", "Ghost", "chv", Seed.Shai), CancellationToken.None);

        Assert.NotNull(on);
        Assert.Null(off);
        Assert.Null(ghost);
    }
}
