using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HealthConnect.Api.Tests;

/// <summary>Starts the real API in memory, pointed at the database we give it (or at none).</summary>
public sealed class ApiFactory(string? connectionString = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }

        // Fail on wiring mistakes (a singleton using a per-request service) the way Development does, not only on a laptop.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
    }
}
