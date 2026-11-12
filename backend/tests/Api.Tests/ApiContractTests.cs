using System.Net;
using System.Text.Json;

namespace HealthConnect.Api.Tests;

/// <summary>
/// The API describes itself (OpenAPI) only in Development. This starts it that way and checks the contract the frontend and
/// the report read: every sign-in endpoint is there, the Bearer token is defined, and only the endpoints that need it are locked.
/// </summary>
[IntegrationTest]
public sealed class ApiContractTests
{
    private static async Task<JsonElement> ContractAsync()
    {
        // No database: describing the API must not need one (the build does it on a machine with none). The /test/ paths are the tests' own probes.
        await using var factory = new ApiFactory(migrate: false, environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None)).RootElement.Clone();
    }

    [Fact]
    public async Task The_contract_lists_every_endpoint_so_far()
    {
        var paths = (await ContractAsync()).GetProperty("paths").EnumerateObject().Select(p => p.Name).Where(name => !name.StartsWith("/test/", StringComparison.Ordinal)).Order().ToList();

        Assert.Equal(
            [
                "/api/appointments", "/api/appointments/{id}", "/api/audit-events", "/api/audit-events/verify", "/api/auth/code", "/api/auth/me", "/api/auth/me/photo", "/api/auth/verify",
                "/api/calls/{appointmentId}/ticket", "/api/media/{id}/upload-url", "/api/patients", "/api/patients/{id}", "/api/payments", "/api/payments/{appointmentId}", "/api/payments/{appointmentId}/checkout", "/api/surveillance/weekly", "/api/vitals", "/api/vitals/{id}", "/health",
            ],
            paths);
    }

    [Fact]
    public async Task The_contract_defines_the_bearer_token()
    {
        var scheme = (await ContractAsync()).GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task Only_the_endpoints_that_need_a_token_are_locked()
    {
        var paths = (await ContractAsync()).GetProperty("paths");

        Assert.True(paths.GetProperty("/api/auth/me").GetProperty("get").TryGetProperty("security", out var locked));
        Assert.NotEqual(0, locked.GetArrayLength());
        Assert.False(paths.GetProperty("/api/auth/code").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/auth/verify").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/health").GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task The_sign_in_answers_are_documented_with_the_statuses_the_app_handles()
    {
        var paths = (await ContractAsync()).GetProperty("paths");

        var verify = paths.GetProperty("/api/auth/verify").GetProperty("post").GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToList();
        var code = paths.GetProperty("/api/auth/code").GetProperty("post").GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToList();

        Assert.Equal(["200", "401", "422", "429"], verify);
        Assert.Equal(["204", "404", "422", "429", "503"], code);
    }

    [Fact]
    public async Task In_Development_there_is_a_page_to_try_the_API_in_the_browser()
    {
        await using var factory = new ApiFactory(migrate: false, environment: "Development");
        using var client = factory.CreateClient();

        var page = await client.GetAsync("/swagger/index.html", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }
}
