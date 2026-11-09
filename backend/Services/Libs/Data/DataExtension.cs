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
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        return services;
    }
}
