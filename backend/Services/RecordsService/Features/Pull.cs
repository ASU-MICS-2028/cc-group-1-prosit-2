using System.Security.Claims;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Store;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HealthConnect.RecordsService.Features;

/// <summary>
/// The pull side of sync: how a worker's phone gets what other workers saved (a nurse sees the CHV's referrals). Each call returns what
/// the caller may see (the database applies the district rules), oldest change first, at most <see cref="Limit"/> at a time. To get
/// the next batch, ask again with the last record's updatedAt (createdAt for vitals) as updatedSince.
/// </summary>
public static class Pull
{
    public const int Limit = 500;

    internal static async Task<IResult> RunAsync<T>(
        ClaimsPrincipal principal,
        RoleSessions sessions,
        Func<RoleSession, CancellationToken, Task<List<T>>> read,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        return session is null
            ? ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user")
            : Results.Ok(await read(session, cancellationToken));
    }
}

/// <summary>GET /api/patients?updatedSince=: patients changed after a moment.</summary>
public sealed class GetPatients : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/patients", Handle)
            .WithName("GetPatients")
            .WithTags("Patients")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<List<PatientDto>>(StatusCodes.Status200OK);

    public static Task<IResult> Handle(
        ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken, [FromQuery] DateTimeOffset? updatedSince = null) =>
        Pull.RunAsync(principal, sessions, (session, ct) => PatientStore.PullAsync(session, updatedSince, Pull.Limit, ct), cancellationToken);
}

/// <summary>GET /api/vitals?updatedSince=: readings stored after a moment.</summary>
public sealed class GetVitals : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/vitals", Handle)
            .WithName("GetVitals")
            .WithTags("Vitals")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<List<VitalsDto>>(StatusCodes.Status200OK);

    public static Task<IResult> Handle(
        ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken, [FromQuery] DateTimeOffset? updatedSince = null) =>
        Pull.RunAsync(principal, sessions, (session, ct) => VitalsStore.PullAsync(session, updatedSince, Pull.Limit, ct), cancellationToken);
}

/// <summary>GET /api/appointments?updatedSince=: appointments and referrals changed after a moment.</summary>
public sealed class GetAppointments : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/appointments", Handle)
            .WithName("GetAppointments")
            .WithTags("Appointments")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<List<AppointmentDto>>(StatusCodes.Status200OK);

    public static Task<IResult> Handle(
        ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken, [FromQuery] DateTimeOffset? updatedSince = null) =>
        Pull.RunAsync(principal, sessions, (session, ct) => AppointmentStore.PullAsync(session, updatedSince, Pull.Limit, ct), cancellationToken);
}
