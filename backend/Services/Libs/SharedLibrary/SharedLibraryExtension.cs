using HealthConnect.SharedLibrary.Security;
using HealthConnect.SharedLibrary.Sms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HealthConnect.SharedLibrary;

public static class SharedLibraryExtension
{
    public static IServiceCollection AddSharedLibrary(this IServiceCollection services)
    {
        // The clock is injected, so code that depends on time (a code that expires, a token that lapses) can be tested.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISmsSender, LogOnlySmsSender>();
        return services.AddHealthConnectPolicies();
    }
}
