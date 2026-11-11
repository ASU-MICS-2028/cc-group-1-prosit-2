using HealthConnect.SharedLibrary.Security;
using HealthConnect.SharedLibrary.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HealthConnect.SharedLibrary;

public static class SharedLibraryExtension
{
    public static IServiceCollection AddSharedLibrary(this IServiceCollection services)
    {
        // The clock is injected, so code that depends on time (a code that expires, a token that lapses) can be tested.
        services.TryAddSingleton(TimeProvider.System);
        // Texts are only logged unless Sms:Provider says httpsms or arkesel (set from this environment's SMS secret, see
        // SmsSecret) and that provider's settings are there.
        services.AddHttpClient(ArkeselSmsSender.Provider, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(HttpSmsSender.Provider, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton<ISmsSender>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var http = sp.GetRequiredService<IHttpClientFactory>();
            return configuration["Sms:Provider"]?.Trim().ToLowerInvariant() switch
            {
                ArkeselSmsSender.Provider => new ArkeselSmsSender(
                    http.CreateClient(ArkeselSmsSender.Provider), configuration, sp.GetRequiredService<ILogger<ArkeselSmsSender>>()),
                HttpSmsSender.Provider => new HttpSmsSender(
                    http.CreateClient(HttpSmsSender.Provider), configuration, sp.GetRequiredService<ILogger<HttpSmsSender>>()),
                _ => new LogOnlySmsSender(configuration, sp.GetRequiredService<ILogger<LogOnlySmsSender>>()),
            };
        });
        return services.AddHealthConnectPolicies();
    }
}
