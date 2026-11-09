using HealthConnect.PlatformService.Features;
using HealthConnect.SharedLibrary.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.PlatformService;

public static class PlatformServiceExtension
{
    public static IServiceCollection AddPlatformService(this IServiceCollection services)
        => services.AddFeature<GetHealth>();
}
