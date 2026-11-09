using System.Net;
using System.Text.Json;

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
    public async Task Health_is_ok_when_the_database_answers()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthconnect-api", body.RootElement.GetProperty("service").GetString());
        Assert.Equal("Healthy", body.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Health_says_nothing_about_the_database_server_or_login()
    {
        using var client = _factory.CreateClient();

        var text = await client.GetStringAsync("/health", CancellationToken.None);

        Assert.DoesNotContain("Host", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", text, StringComparison.OrdinalIgnoreCase);
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
