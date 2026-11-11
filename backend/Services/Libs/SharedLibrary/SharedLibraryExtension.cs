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
        // Texts are only logged unless Sms:Provider says arkesel (and its key and sender name are set).
        services.AddHttpClient(ArkeselSmsSender.Provider, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton<ISmsSender>(sp =>
            string.Equals(sp.GetRequiredService<IConfiguration>()["Sms:Provider"], ArkeselSmsSender.Provider, StringComparison.OrdinalIgnoreCase)
                ? new ArkeselSmsSender(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(ArkeselSmsSender.Provider),
                    sp.GetRequiredService<IConfiguration>(),
                    sp.GetRequiredService<ILogger<ArkeselSmsSender>>())
                : new LogOnlySmsSender(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<LogOnlySmsSender>>()));
        return services.AddHealthConnectPolicies();
    }
}
