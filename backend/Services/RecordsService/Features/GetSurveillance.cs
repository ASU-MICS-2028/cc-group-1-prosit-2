using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Dapper;
using HealthConnect.Data;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HealthConnect.RecordsService.Features;

/// <summary>One district-week of counts. When fewer than 5 patients were seen, every count is null and suppressed is true, so a small group cannot be identified.</summary>
public sealed record WeeklyCountDto(
    [property: JsonPropertyName("district")] string District,
    [property: JsonPropertyName("week")] string Week,
    [property: JsonPropertyName("patients_seen")] long? PatientsSeen,
    [property: JsonPropertyName("patients_high_bp")] long? PatientsHighBp,
    [property: JsonPropertyName("patients_fever")] long? PatientsFever,
    [property: JsonPropertyName("suppressed")] bool Suppressed);

/// <summary>
/// GET /api/surveillance/weekly: the district summary page's numbers: per district and week, how many patients were seen, had high blood
/// pressure or a fever. Counts only, never a patient: the database view hides any cell under 5, and the district officer and the auditor
/// have no access to patient tables at all.
/// </summary>
public sealed class GetSurveillance : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/surveillance/weekly", Handle)
            .WithName("GetSurveillanceWeekly")
            .WithTags("Surveillance")
            .RequireAuthorization(AuthPolicies.Summary)
            .Produces<List<WeeklyCountDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

    public static async Task<IResult> Handle(ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        if (session is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
        }

        var rows = await session.Connection.QueryAsync<Row>(new CommandDefinition(
            "SELECT district, week, patients_seen, patients_high_bp, patients_fever, suppressed FROM surveillance_weekly ORDER BY district, week",
            transaction: session.Transaction,
            cancellationToken: cancellationToken));
        return Results.Ok(rows.Select(r => new WeeklyCountDto(
            r.District, r.Week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.PatientsSeen, r.PatientsHighBp, r.PatientsFever, r.Suppressed)).ToList());
    }

    private sealed class Row
    {
        public string District { get; set; } = "";

        public DateTime Week { get; set; }

        public long? PatientsSeen { get; set; }

        public long? PatientsHighBp { get; set; }

        public long? PatientsFever { get; set; }

        public bool Suppressed { get; set; }
    }
}
