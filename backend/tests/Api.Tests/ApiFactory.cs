using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HealthConnect.Api.Tests;

/// <summary>Starts the real API in memory, pointed at the database we give it (or at none).</summary>
public sealed class ApiFactory(string? connectionString = null, bool migrate = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }

        // On by default (as in production); a test can switch it off to see the API before its database is migrated.
        builder.UseSetting("Database:MigrateOnStartup", migrate ? "true" : "false");

        // Fail on wiring mistakes (a singleton using a per-request service) the way Development does, not only on a laptop.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
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
}
