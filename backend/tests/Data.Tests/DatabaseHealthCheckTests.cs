using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HealthConnect.Data.Tests;

internal static class HealthCheckHelpers
{
    public static DatabaseHealthCheck CheckFor(Database database) => new(database, NullLogger<DatabaseHealthCheck>.Instance);

    public static DatabaseSettings WithConnectionString(string connectionString) =>
        new(connectionString, null, 5432, null, "healthconnect", null, null, null);
}

/// <summary>The health check's behaviour that needs no database at all.</summary>
[UnitTest]
public sealed class DatabaseHealthCheckUnitTests
{
    [Fact]
    public async Task Is_unhealthy_when_the_database_is_not_configured()
    {
        var settings = new DatabaseSettings(null, null, 5432, null, "healthconnect", null, null, null);
        await using var database = new Database(settings, Substitute.For<IDatabaseSecretReader>());

        var result = await HealthCheckHelpers.CheckFor(database).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("not configured", result.Description);
    }

    [Fact]
    public async Task Does_not_swallow_the_caller_hanging_up()
    {
        await using var database = new Database(
            HealthCheckHelpers.WithConnectionString("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x"),
            Substitute.For<IDatabaseSecretReader>());
        using var hungUp = new CancellationTokenSource();
        await hungUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await HealthCheckHelpers.CheckFor(database).CheckHealthAsync(new HealthCheckContext(), hungUp.Token));
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}

/// <summary>The health check against a real PostgreSQL in Docker, and against ways the database can be out of reach.</summary>
[IntegrationTest]
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseHealthCheckIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Is_healthy_when_PostgreSQL_answers()
    {
        await using var database = new Database(
            HealthCheckHelpers.WithConnectionString(postgres.ConnectionString), Substitute.For<IDatabaseSecretReader>());

        var result = await HealthCheckHelpers.CheckFor(database).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Is_unhealthy_and_quick_when_nothing_is_listening()
    {
        await using var database = new Database(
            HealthCheckHelpers.WithConnectionString("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"),
            Substitute.For<IDatabaseSecretReader>());
        var clock = Stopwatch.StartNew();

        var result = await HealthCheckHelpers.CheckFor(database).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Is_unhealthy_when_the_password_cannot_be_read_from_the_secret()
    {
        var secrets = Substitute.For<IDatabaseSecretReader>();
        secrets.GetPasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<string>>(_ => throw new InvalidOperationException("AWS said no"));
        var settings = new DatabaseSettings(null, "127.0.0.1", 1, "x", "healthconnect", "arn:x", "/ca.pem", "af-south-1");
        await using var database = new Database(settings, secrets);

        var result = await HealthCheckHelpers.CheckFor(database).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
