using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.Data;

public static class DataExtension
{
    public static IServiceCollection AddData(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = DatabaseSettings.From(configuration);

        services.AddSingleton(settings);
        services.AddSingleton<IDatabaseSecretReader>(_ => SecretsManagerSecretReader.For(settings));
        services.AddSingleton<Database>();
        services.AddSingleton<RoleSessions>();

        // EF Core uses the same connection pool (and so the same Secrets Manager password and TLS rules) as everything else.
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<Database>().DataSource));

        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.AddHostedService<DatabaseMigrator>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<MigrationsHealthCheck>("schema");

        return services;
    }
}
