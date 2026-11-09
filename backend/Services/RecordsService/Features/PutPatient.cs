using System.Security.Claims;
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
/// PUT /api/patients/{id}: saves a patient the phone made. The id is the phone's own, so sending the same patient twice (a lost
/// reply on 2G) saves it once. The version decides what happens when two workers changed the same patient:
/// <list type="bullet">
/// <item>not on the server yet: created, 201;</item>
/// <item>the phone's version is the server's: updated, version + 1, 200;</item>
/// <item>a different version but the same content: it is a repeat, nothing changes, 200;</item>
/// <item>a different version and different content: 409 with what the server holds, and the app shows "Needs review".</item>
/// </list>
/// Nothing is saved without consent. A CHV can only save in their own district (the database enforces it too).
/// </summary>
public sealed partial class PutPatient : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/patients/{id:guid}", Handle)
            .WithName("PutPatient")
            .WithTags("Patients")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Accepts<PatientDto>("application/json")
            .Produces<PatientDto>(StatusCodes.Status200OK)
            .Produces<PatientDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        Guid id,
        PatientDto patient,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        TimeProvider time,
        ILogger<PutPatient> logger,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (errors, phone) = PatientValidator.Check(id, patient, now);
        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        if (session is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
        }

        if (user.Role == Roles.Chv && !string.Equals(patient.District.Trim(), user.District, StringComparison.Ordinal))
        {
            LogRefused(logger, user.Id, id, "another district");
            return ApiResults.Error(StatusCodes.Status403Forbidden, "wrong_district");
        }

        var nationalId = string.IsNullOrWhiteSpace(patient.NationalId) ? null : patient.NationalId.Trim();
        var incoming = patient with { Phone = phone, NationalId = nationalId };

        try
        {
            var existing = await PatientStore.FindAsync(session, id, cancellationToken);
            if (existing is null)
            {
                return await CreateAsync(session, id, incoming, now, logger, cancellationToken);
            }

            if (existing.Dto.Version == patient.Version)
            {
                return await UpdateAsync(session, existing, incoming, logger, cancellationToken);
            }

            return PatientStore.SameContent(existing.Dto, incoming with { NationalId = nationalId ?? existing.Dto.NationalId })
                ? Results.Ok(existing.Dto)
                : ApiResults.Conflict(existing.Dto);
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege
            || (refusal.SqlState == PostgresErrorCodes.UniqueViolation && refusal.ConstraintName?.Contains("fhir_id", StringComparison.Ordinal) == true))
        {
            // Row-level security refused it, or the patient exists in a district this worker cannot see.
            LogRefused(logger, user.Id, id, "the database refused it");
            return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden");
        }
    }

    private static async Task<IResult> CreateAsync(
        RoleSession session, Guid id, PatientDto patient, DateTimeOffset now, ILogger logger, CancellationToken cancellationToken)
    {
        var createdAt = patient.CreatedAt is { } given && given <= now.AddDays(1) && given.Year >= 2020 ? given : now;
        var internalId = await PatientStore.InsertAsync(session, id, patient, patient.Phone, createdAt, cancellationToken);
        await PatientStore.AddConsentAsync(session, internalId, id, patient.Consent, cancellationToken);
        if (patient.NationalId is not null && !await PatientStore.SetNationalIdAsync(session, internalId, patient.NationalId, cancellationToken))
        {
            return ApiResults.Validation("nationalId", "This national id is already registered to another patient.");
        }

        var saved = (await PatientStore.FindAsync(session, id, cancellationToken))!;
        await session.CommitAsync(cancellationToken);
        LogSaved(logger, session.User.Id, id, "created");
        return Results.Json(saved.Dto, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        RoleSession session, StoredPatient existing, PatientDto patient, ILogger logger, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(existing.Dto.Id);
        if (!await PatientStore.UpdateAsync(session, existing.InternalId, existing.Dto.Version, patient, patient.Phone, cancellationToken))
        {
            return ApiResults.Conflict(existing.Dto);   // someone else saved it a moment ago
        }

        if (!SameConsent(existing.Dto.Consent, patient.Consent))
        {
            await PatientStore.AddConsentAsync(session, existing.InternalId, id, patient.Consent, cancellationToken);
        }

        // A worker cannot clear a national id (nothing is deleted); a new one replaces it and the old one is kept as secondary.
        if (patient.NationalId is not null && patient.NationalId != existing.Dto.NationalId
            && !await PatientStore.SetNationalIdAsync(session, existing.InternalId, patient.NationalId, cancellationToken))
        {
            return ApiResults.Validation("nationalId", "This national id is already registered to another patient.");
        }

        var saved = (await PatientStore.FindAsync(session, id, cancellationToken))!;
        await session.CommitAsync(cancellationToken);
        LogSaved(logger, session.User.Id, id, "updated");
        return Results.Ok(saved.Dto);
    }

    private static bool SameConsent(ConsentDto a, ConsentDto b) =>
        a.Method == b.Method && a.At.ToUniversalTime() == b.At.ToUniversalTime() && a.StatementVersion == b.StatementVersion
        && a.Language == b.Language && (a.Witness ?? string.Empty) == (b.Witness ?? string.Empty) && a.Evidence == b.Evidence;

    [LoggerMessage(Level = LogLevel.Information, Message = "Patient {PatientId} {What} by {UserId}")]
    private static partial void LogSaved(ILogger logger, string userId, Guid patientId, string what);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Patient save refused for {UserId} on {PatientId}: {Reason}")]
    private static partial void LogRefused(ILogger logger, string userId, Guid patientId, string reason);
}
