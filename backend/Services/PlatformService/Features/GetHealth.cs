using HealthConnect.PlatformService.Models;
using HealthConnect.SharedLibrary.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthConnect.PlatformService.Features;

/// <summary>
/// GET /health: 200 only when the API is up AND the database answers, otherwise 503.
/// The deploy pipeline waits for this (through nginx as /api/health) and rolls the release back if it never turns 200.
/// </summary>
public sealed class GetHealth : IFeature
{
    public const string ServiceName = "healthconnect-api";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", Handle)
            .WithName("GetHealth")
            .WithTags("Platform")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);
    }

    public static async Task<IResult> Handle([FromServices] HealthCheckService health, CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(cancellationToken);
        var healthy = report.Status == HealthStatus.Healthy;
        var body = new HealthResponse(
            healthy ? "ok" : "unhealthy",
            ServiceName,
            report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()));

        return healthy
            ? TypedResults.Ok(body)
            : TypedResults.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
