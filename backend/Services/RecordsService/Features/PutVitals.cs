using System.Security.Claims;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Store;
using HealthConnect.RecordsService.Validation;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HealthConnect.RecordsService.Features;

/// <summary>
/// PUT /api/vitals/{id}: saves one reading the phone took. A reading is never edited (a wrong one is marked entered-in-error, never
/// deleted), so sending the same reading again is a repeat (200), and sending a different one under the same id is a conflict (409).
/// Every number must be plausible; blood pressure comes as a pair. The patient must already be on the server: if the patient's own
/// save was refused or has not arrived yet, this answers 422 and the phone keeps the reading to try again.
/// </summary>
public sealed partial class PutVitals : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/vitals/{id:guid}", Handle)
            .WithName("PutVitals")
            .WithTags("Vitals")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Accepts<VitalsDto>("application/json")
            .Produces<VitalsDto>(StatusCodes.Status200OK)
            .Produces<VitalsDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        Guid id,
        VitalsDto vitals,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        TimeProvider time,
        ILogger<PutVitals> logger,
        CancellationToken cancellationToken)
    {
        var errors = VitalsValidator.Check(id, vitals, time.GetUtcNow());
        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var session = await sessions.TryBeginAsync(user, cancellationToken);
            if (session is null)
            {
                return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
            }

            try
            {
                var existing = await VitalsStore.FindAsync(session, id, cancellationToken);
                if (existing is not null)
                {
                    return VitalsStore.SameContent(existing, vitals) ? Results.Ok(existing) : ApiResults.Conflict(existing);
                }

                var patientInternalId = await session.Connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT id FROM patients WHERE fhir_id = @patient", new { patient = Guid.Parse(vitals.PatientId) },
                    session.Transaction, cancellationToken: cancellationToken));
                if (patientInternalId is null)
                {
                    return ApiResults.Validation("patientId", "This patient is not on the server yet.");
                }

                await VitalsStore.InsertAsync(session, id, patientInternalId.Value, vitals, cancellationToken);
                var saved = (await VitalsStore.FindAsync(session, id, cancellationToken))!;
                await session.CommitAsync(cancellationToken);
                LogSaved(logger, user.Id, id);
                return Results.Json(saved, statusCode: StatusCodes.Status201Created);
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // The same reading arrived twice at the same moment and the other one won: read it back and answer as for a repeat.
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden");
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.RaiseException)
            {
                // The database's own plausibility check said no (the API checks first, so this is the last line of defence).
                return ApiResults.Validation("vitals", "A measurement is outside what is possible.");
            }
        }

        return ApiResults.Error(StatusCodes.Status409Conflict, "conflict");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Vitals {VitalsId} saved by {UserId}")]
    private static partial void LogSaved(ILogger logger, string userId, Guid vitalsId);
}
