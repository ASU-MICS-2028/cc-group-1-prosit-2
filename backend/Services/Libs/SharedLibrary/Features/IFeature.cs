using Microsoft.AspNetCore.Routing;

namespace HealthConnect.SharedLibrary.Features;

/// <summary>
/// One use case, one class. A feature maps its own route and its handler takes what it needs
/// (database, the signed-in user, the clock) as parameters, so a unit test can call the handler directly.
/// Register it in the service's AddXService() with AddFeature.
/// </summary>
public interface IFeature
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
