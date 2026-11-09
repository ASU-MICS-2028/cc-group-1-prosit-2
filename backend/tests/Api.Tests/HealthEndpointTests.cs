using System.Net;
using System.Text.Json;
using Npgsql;

namespace HealthConnect.Api.Tests;

/// <summary>/health with a real PostgreSQL in a container (needs Docker running).</summary>
[IntegrationTest]
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _factory = new ApiFactory(_postgres.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Health_is_ok_once_the_API_has_migrated_its_database()
    {
        using var client = _factory.CreateClient();

        var response = await ApiFactory.HealthWhenReadyAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthconnect-api", body.RootElement.GetProperty("service").GetString());
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetString());
        Assert.Equal("Healthy", checks.GetProperty("schema").GetString());
    }

    [Fact]
    public async Task Starting_the_API_creates_the_tables()
    {
        using var client = _factory.CreateClient();
        await ApiFactory.HealthWhenReadyAsync(client);

        await using var source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var command = source.CreateCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('patients', 'vitals', 'appointments', 'users', 'media', '__EFMigrationsHistory')");

        Assert.Equal(6L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Health_says_nothing_about_the_database_server_or_login()
    {
        using var client = _factory.CreateClient();
        await ApiFactory.HealthWhenReadyAsync(client);

        var text = await client.GetStringAsync("/health", CancellationToken.None);

        Assert.DoesNotContain("Host", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", text, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A release whose database changes have not been applied must never be called healthy: the deploy would keep it.</summary>
[IntegrationTest]
public sealed class HealthBeforeMigrationsTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public Task InitializeAsync() => _postgres.InitializeAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public async Task Health_is_503_while_the_database_answers_but_the_migrations_are_not_applied()
    {
        await using var factory = new ApiFactory(_postgres.ConnectionString, migrate: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetString());
        Assert.Equal("Unhealthy", checks.GetProperty("schema").GetString());
    }
}

/// <summary>The API when the database is missing or out of reach: it must still start, and say so honestly.</summary>
[IntegrationTest]
public sealed class ApiWithoutDatabaseTests
{
    [Fact]
    public async Task Health_is_503_when_no_database_is_configured()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Health_is_503_when_the_database_is_out_of_reach()
    {
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_address_is_a_404()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/nothing-here", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task The_API_does_not_describe_itself_outside_development(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
