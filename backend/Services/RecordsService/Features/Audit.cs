using System.Security.Claims;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace HealthConnect.RecordsService.Features;

/// <summary>What the audit endpoints accept and how a phone's audit event becomes a row of the server's access_log.</summary>
public static class Audit
{
    public const int MaxBatch = 500;

    private static readonly string[] Actions = ["view", "create", "update", "denied"];
    private static readonly string[] Outcomes = ["success", "denied"];
    private static readonly string[] Purposes = ["TREAT", "HOPERAT"];

    /// <summary>The access_log action for the app's: viewing is a read, creating and updating are writes, and a refusal is a refused read.</summary>
    internal static string LogAction(string action) => action is "create" or "update" ? "write" : "read";

    internal static Dictionary<string, string> Check(IReadOnlyList<AuditEventDto>? events, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();
        if (events is null || events.Count == 0)
        {
            errors["events"] = "Send at least one event.";
            return errors;
        }

        if (events.Count > MaxBatch)
        {
            errors["events"] = $"Send at most {MaxBatch} events at a time.";
            return errors;
        }

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var at = $"events[{i}]";
            if (!Guid.TryParse(e.Id, out _))
            {
                errors[$"{at}.id"] = "Must be a UUID.";
            }

            if (!Actions.Contains(e.Action))
            {
                errors[$"{at}.action"] = "Must be view, create, update or denied.";
            }

            if (!Outcomes.Contains(e.Outcome))
            {
                errors[$"{at}.outcome"] = "Must be success or denied.";
            }

            if (!Purposes.Contains(e.Purpose))
            {
                errors[$"{at}.purpose"] = "Must be TREAT or HOPERAT.";
            }

            if (string.IsNullOrWhiteSpace(e.Entity) || e.Entity.Length > 40)
            {
                errors[$"{at}.entity"] = "Required, at most 40 characters.";
            }

            if (e.EntityId is { Length: > 100 })
            {
                errors[$"{at}.entityId"] = "At most 100 characters.";
            }

            if (e.PatientId is not null && !Guid.TryParse(e.PatientId, out _))
            {
                errors[$"{at}.patientId"] = "Must be a UUID.";
            }

            if (e.Detail is { Length: > 500 })
            {
                errors[$"{at}.detail"] = "At most 500 characters.";
            }

            if (e.At > now.AddDays(1) || e.At < now.AddYears(-5))
            {
                errors[$"{at}.at"] = "Must be a believable time.";
            }
        }

        return errors;
    }
}

/// <summary>
/// POST /api/audit-events: the phone uploads the access events it recorded (who saw or changed what, even while offline). The server
/// writes each into its hash-chained, append-only log as the signed-in worker: who and which role come from the token, never from the
/// request, so nobody can write an entry in someone else's name. An event sent twice is stored once. Answers 204.
/// </summary>
public sealed partial class PostAuditEvents : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/audit-events", Handle)
            .WithName("PostAuditEvents")
            .WithTags("Audit")
            .RequireAuthorization(AuthPolicies.AnyWorker)
            .Accepts<AuditUploadRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        AuditUploadRequest request,
        ClaimsPrincipal principal,
        HttpContext? http,
        RoleSessions sessions,
        TimeProvider time,
        ILogger<PostAuditEvents> logger,
        CancellationToken cancellationToken)
    {
        var events = request?.Events;
        var errors = Audit.Check(events, time.GetUtcNow());
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

        var ip = http?.Connection.RemoteIpAddress?.ToString();
        var saved = 0;
        foreach (var e in events!)
        {
            var outcome = e.Action == "denied" ? "denied" : e.Outcome;
            saved += await session.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO access_log (accessed_by, role, action, outcome, purpose_of_use, source_ip, client_event_id, patient_uuid, entity, entity_id, detail, client_at)
                VALUES (@by, @role, @action, @outcome, @purpose, CAST(@ip AS inet), @eventId, @patient, @entity, @entityId, @detail, @clientAt)
                ON CONFLICT DO NOTHING
                """,
                new
                {
                    by = user.Id,
                    role = user.Role,
                    action = Audit.LogAction(e.Action),
                    outcome,
                    purpose = e.Purpose,
                    ip,
                    eventId = Guid.Parse(e.Id),
                    patient = e.PatientId is null ? (Guid?)null : Guid.Parse(e.PatientId),
                    entity = e.Entity,
                    entityId = e.EntityId,
                    detail = e.Detail,
                    clientAt = e.At,
                },
                session.Transaction,
                cancellationToken: cancellationToken));
        }

        await session.CommitAsync(cancellationToken);
        LogUploaded(logger, user.Id, events.Count, saved);
        return Results.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserId} uploaded {Received} audit events, {Saved} new")]
    private static partial void LogUploaded(ILogger logger, string userId, int received, int saved);
}

/// <summary>GET /api/audit-events: the activity log, for the auditor only. Newest first; optionally one patient's.</summary>
public sealed class GetAuditEvents : IFeature
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/audit-events", Handle)
            .WithName("GetAuditEvents")
            .WithTags("Audit")
            .RequireAuthorization(AuthPolicies.Auditor)
            .Produces<List<AuditEntryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

    public static async Task<IResult> Handle(
        ClaimsPrincipal principal,
        RoleSessions sessions,
        CancellationToken cancellationToken,
        [FromQuery] Guid? patientId = null,
        [FromQuery] int? limit = null)
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

        var rows = await session.Connection.QueryAsync<AuditRow>(new CommandDefinition(
            """
            SELECT id, accessed_by AS actor_id, role, action, outcome, purpose_of_use AS purpose, entity, entity_id,
                   patient_uuid::text AS patient_id, detail, host(source_ip) AS source_ip,
                   accessed_at AS recorded_at, client_at AS happened_at
            FROM access_log
            WHERE (@patientId::uuid IS NULL OR patient_uuid = @patientId)
            ORDER BY id DESC
            LIMIT @limit
            """,
            new { patientId, limit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit) },
            session.Transaction,
            cancellationToken: cancellationToken));
        return Results.Ok(rows.Select(r => new AuditEntryDto(
            r.Id, r.ActorId, r.Role, r.Action, r.Outcome, r.Purpose, r.Entity, r.EntityId, r.PatientId, r.Detail, r.SourceIp,
            new DateTimeOffset(DateTime.SpecifyKind(r.RecordedAt, DateTimeKind.Utc)),
            r.HappenedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null)).ToList());
    }

    private sealed class AuditRow
    {
        public int Id { get; set; }

        public string ActorId { get; set; } = "";

        public string? Role { get; set; }

        public string Action { get; set; } = "";

        public string Outcome { get; set; } = "";

        public string Purpose { get; set; } = "";

        public string? Entity { get; set; }

        public string? EntityId { get; set; }

        public string? PatientId { get; set; }

        public string? Detail { get; set; }

        public string? SourceIp { get; set; }

        public DateTime RecordedAt { get; set; }

        public DateTime? HappenedAt { get; set; }
    }
}

/// <summary>GET /api/audit-events/verify: recomputes the log's hash chain and says whether anything was changed behind the database's back.</summary>
public sealed class VerifyAuditEvents : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/audit-events/verify", Handle)
            .WithName("VerifyAuditEvents")
            .WithTags("Audit")
            .RequireAuthorization(AuthPolicies.Auditor)
            .Produces<AuditVerification>(StatusCodes.Status200OK)
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

        var (firstBad, checkedCount) = await session.Connection.QuerySingleAsync<(int? FirstBadId, long EntriesChecked)>(new CommandDefinition(
            "SELECT first_bad_id, entries_checked FROM verify_access_log()", transaction: session.Transaction, cancellationToken: cancellationToken));
        return Results.Ok(new AuditVerification(firstBad is null, checkedCount, firstBad));
    }
}
