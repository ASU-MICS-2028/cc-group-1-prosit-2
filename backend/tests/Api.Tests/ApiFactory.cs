using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.Api.Tests;

/// <summary>Starts the real API in memory, pointed at the database we give it (or at none).</summary>
public sealed class ApiFactory(
    string? connectionString = null,
    bool migrate = true,
    IReadOnlyDictionary<string, string>? settings = null,
    Action<IServiceCollection>? configureServices = null,
    string environment = "Testing") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }

        // On by default (as in production); a test can switch it off to see the API before its database is migrated.
        builder.UseSetting("Database:MigrateOnStartup", migrate ? "true" : "false");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        // Fail on wiring mistakes (a singleton using a per-request service) the way Development does, not only on a laptop.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        builder.ConfigureServices(services => services.AddFeature<PolicyProbes>());
        builder.ConfigureTestServices(services => configureServices?.Invoke(services));
    }

    /// <summary>The API migrates its database in the background when it starts; wait until /health says everything is ready.</summary>
    public static async Task<HttpResponseMessage> HealthWhenReadyAsync(HttpClient client)
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        HttpResponseMessage response;
        do
        {
            response = await client.GetAsync("/health", CancellationToken.None);
            if (response.IsSuccessStatusCode || DateTime.UtcNow > until)
            {
                break;
            }

            await Task.Delay(200);
        }
        while (true);

        return response;
    }

    /// <summary>One endpoint per access rule, so a test can see which roles each rule lets through.</summary>
    private sealed class PolicyProbes : IFeature
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            foreach (var (path, policy) in new[]
            {
                ("clinical", AuthPolicies.Clinical),
                ("clinician", AuthPolicies.Clinician),
                ("summary", AuthPolicies.Summary),
                ("auditor", AuthPolicies.Auditor),
            })
            {
                app.MapGet($"/test/{path}", () => Results.Ok("ok")).RequireAuthorization(policy);
            }
        }
    }
}
