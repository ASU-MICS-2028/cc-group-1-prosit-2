using HealthConnect.Data.Persistence;
using HealthConnect.Data.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace HealthConnect.Data.Tests;

/// <summary>What the API does with its database when it starts: set it up, wait for it, say honestly whether it is ready.</summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class DatabaseMigratorTests(SchemaFixture fixture)
{
    private static ServiceProvider ProviderFor(string? connectionString, bool migrate = true, double retrySeconds = 0.1)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = migrate.ToString(),
            ["Database:MigrationRetrySeconds"] = retrySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (connectionString is not null)
        {
            settings["ConnectionStrings:Default"] = connectionString;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddData(configuration).BuildServiceProvider();
    }

    private static DatabaseMigrator MigratorOf(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<DatabaseMigrator>().Single();

    private static async Task RunToEndAsync(DatabaseMigrator migrator)
    {
        await migrator.StartAsync(CancellationToken.None);
        await migrator.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
        await migrator.StopAsync(CancellationToken.None);
    }

    private static async Task<int> PendingAsync(string connectionString)
    {
        await using var db = SchemaFixture.NewContext(connectionString);
        return (await db.Database.GetPendingMigrationsAsync()).Count();
    }

    [Fact]
    public async Task It_sets_up_a_new_database_when_the_API_starts()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_migrator_new");
        await using var provider = ProviderFor(connection);

        await RunToEndAsync(MigratorOf(provider));

        Assert.Equal(0, await PendingAsync(connection));
    }

    [Fact]
    public async Task It_keeps_trying_while_the_database_cannot_be_reached_and_sets_it_up_when_it_can()
    {
        // The login does not exist yet (like a database server that is still starting): every attempt is refused.
        var late = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = "late_user",
            Password = "late-pw",
            Database = "hc_migrator_late",
        }.ConnectionString;
        await using var provider = ProviderFor(late, retrySeconds: 0.2);
        var migrator = MigratorOf(provider);
        await migrator.StartAsync(CancellationToken.None);

        await Task.Delay(700); // it has been refused and has tried again a few times by now
        Assert.False(migrator.ExecuteTask!.IsCompleted);
        await using (var admin = fixture.Admin.CreateCommand("CREATE ROLE late_user LOGIN SUPERUSER PASSWORD 'late-pw'"))
        {
            await admin.ExecuteNonQueryAsync();
        }

        await migrator.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(30));
        await migrator.StopAsync(CancellationToken.None);

        Assert.Equal(0, await PendingAsync(late));
    }

    [Fact]
    public async Task It_stops_cleanly_when_the_API_shuts_down_while_still_waiting()
    {
        var never = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = "hc_migrator_never" }.ConnectionString;
        await using var provider = ProviderFor(never, retrySeconds: 0.1);
        var migrator = MigratorOf(provider);
        await migrator.StartAsync(CancellationToken.None);
        await Task.Delay(400);

        await migrator.StopAsync(CancellationToken.None);

        Assert.True(migrator.ExecuteTask!.IsCompleted);
        Assert.False(migrator.ExecuteTask.IsFaulted);
    }

    [Fact]
    public async Task It_does_nothing_when_switched_off()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_migrator_off");
        await using var provider = ProviderFor(connection, migrate: false);

        await RunToEndAsync(MigratorOf(provider));

        Assert.Equal(9, await PendingAsync(connection));
    }

    [Fact]
    public async Task It_does_nothing_when_there_is_no_database_configured()
    {
        await using var provider = ProviderFor(null);

        await RunToEndAsync(MigratorOf(provider)); // returns at once instead of retrying forever
    }

    [Fact]
    public async Task The_schema_check_follows_the_migrations()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_schema_check");
        await using var provider = ProviderFor(connection, migrate: false);
        var check = provider.GetRequiredService<HealthCheckService>();

        var before = await check.CheckHealthAsync();
        await SchemaFixture.MigrateAsync(connection);
        var after = await check.CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, before.Entries["database"].Status);
        Assert.Equal(HealthStatus.Unhealthy, before.Entries["schema"].Status);
        Assert.Contains("9 migration(s) not applied", before.Entries["schema"].Description);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }

    [Fact]
    public async Task The_schema_check_is_unhealthy_when_the_database_is_out_of_reach_or_not_configured()
    {
        await using var unreachable = ProviderFor("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
        await using var none = ProviderFor(null);

        var down = await unreachable.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var missing = await none.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, down.Entries["schema"].Status);
        Assert.Equal("not configured", missing.Entries["schema"].Description);
    }

    [Fact]
    public async Task The_schema_check_does_not_swallow_the_caller_hanging_up()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_schema_cancel");
        await using var provider = ProviderFor(connection, migrate: false);
        var check = new MigrationsHealthCheck(
            provider.GetRequiredService<Database>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationsHealthCheck>.Instance);
        using var hungUp = new CancellationTokenSource();
        await hungUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new HealthCheckContext(), hungUp.Token));
    }

    [Fact]
    public async Task AddData_makes_the_database_context_use_the_same_connection_as_everything_else()
    {
        var connection = await fixture.CreateDatabaseAsync("hc_context");
        await using var provider = ProviderFor(connection, migrate: false);
        await using var scope = provider.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.Database.CanConnectAsync());
        Assert.Same(provider.GetRequiredService<DatabaseSettings>(), provider.GetRequiredService<DatabaseSettings>());
    }
}
