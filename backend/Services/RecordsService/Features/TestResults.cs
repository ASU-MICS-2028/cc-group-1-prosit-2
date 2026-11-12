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
using Npgsql;

namespace HealthConnect.RecordsService.Features;

/// <summary>
/// PUT /api/results/{id}: saves a test the patient needs or had. Most villages have no laboratory or X-ray machine, so a test is often
/// requested first (the patient goes to a facility that has one) and resulted later, when the patient comes back with the slip or the
/// film and the CHV photographs it. A quick test done on the spot is saved as resulted straight away.
/// <list type="bullet">
/// <item>Any clinical worker may save one for a patient they can see (the database applies the district rules).</item>
/// <item>Requested can become resulted or cancelled; a result can be corrected; a cancelled test stays cancelled.</item>
/// <item>The patient, the category and the test name of a saved test do not change; request a new one instead.</item>
/// </list>
/// </summary>
public sealed partial class PutTestResult : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/results/{id:guid}", Handle)
            .WithName("PutTestResult")
            .WithTags("Results")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Accepts<TestResultDto>("application/json")
            .Produces<TestResultDto>(StatusCodes.Status200OK)
            .Produces<TestResultDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static readonly string[] Categories = ["rapid", "lab", "imaging"];
    public static readonly string[] Statuses = ["requested", "resulted", "cancelled"];

    public static Dictionary<string, string> Check(Guid id, TestResultDto r, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();
        if (r.Id != id.ToString())
        {
            errors["id"] = "Must match the address.";
        }

        if (!Guid.TryParse(r.PatientId, out _))
        {
            errors["patientId"] = "Must be a UUID.";
        }

        if (!Categories.Contains(r.Category))
        {
            errors["category"] = "Must be rapid, lab or imaging.";
        }

        if (string.IsNullOrWhiteSpace(r.Test) || r.Test.Trim().Length > 120)
        {
            errors["test"] = "Required, at most 120 characters.";
        }

        if (!Statuses.Contains(r.Status))
        {
            errors["status"] = "Must be requested, resulted or cancelled.";
        }

        if (r.Status == "resulted" && r.ResultedAt is null)
        {
            errors["resultedAt"] = "A result needs the time it was known.";
        }

        if (r.Status != "resulted" && r.ResultedAt is not null)
        {
            errors["resultedAt"] = "Only a result has a result time.";
        }

        if (r.Flag is not (null or "normal" or "abnormal"))
        {
            errors["flag"] = "Must be normal or abnormal.";
        }

        if (r.Facility is { Length: > 200 })
        {
            errors["facility"] = "At most 200 characters.";
        }

        if (r.Result is { Length: > 2000 })
        {
            errors["result"] = "At most 2000 characters.";
        }

        if (r.Notes is { Length: > 2000 })
        {
            errors["notes"] = "At most 2000 characters.";
        }

        if (r.RequestedAt > now.AddDays(1) || r.RequestedAt < now.AddYears(-5))
        {
            errors["requestedAt"] = "Must be a believable time.";
        }

        if (r.ResultedAt is { } at && (at > now.AddDays(1) || at < r.RequestedAt.AddDays(-1)))
        {
            errors["resultedAt"] = "Must be after the request and not in the future.";
        }

        return errors;
    }

    /// <summary>Whether a saved test may move from one status to another.</summary>
    public static bool MayMove(string from, string to) =>
        from == to ? from != "cancelled" : from == "requested" && to is "resulted" or "cancelled";

    public static async Task<IResult> Handle(
        Guid id,
        TestResultDto result,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        TimeProvider time,
        ILogger<PutTestResult> logger,
        CancellationToken cancellationToken)
    {
        var errors = Check(id, result, time.GetUtcNow());
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
                var existing = await TestResultStore.FindAsync(session, id, cancellationToken);
                if (existing is null)
                {
                    var patientInternalId = await session.Connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                        "SELECT id FROM patients WHERE fhir_id = @patient", new { patient = Guid.Parse(result.PatientId) },
                        session.Transaction, cancellationToken: cancellationToken));
                    if (patientInternalId is null)
                    {
                        return ApiResults.Validation("patientId", "This patient is not on the server yet.");
                    }

                    await TestResultStore.InsertAsync(session, id, patientInternalId.Value, result, cancellationToken);
                    var created = (await TestResultStore.FindAsync(session, id, cancellationToken))!;
                    await session.CommitAsync(cancellationToken);
                    LogSaved(logger, user.Id, id, result.Status);
                    return Results.Json(created, statusCode: StatusCodes.Status201Created);
                }

                if (result.PatientId != existing.PatientId || result.Category != existing.Category || result.Test.Trim() != existing.Test)
                {
                    return ApiResults.Validation("test", "The patient, category and test of a saved test cannot change; request a new one.");
                }

                if (existing.Version != result.Version)
                {
                    return TestResultStore.SameContent(existing, result) ? Results.Ok(existing) : ApiResults.Conflict(existing);
                }

                if (!MayMove(existing.Status, result.Status))
                {
                    return ApiResults.Conflict(existing);
                }

                if (!await TestResultStore.UpdateAsync(session, id, existing.Version, result, cancellationToken))
                {
                    return ApiResults.Conflict(existing);
                }

                var updated = (await TestResultStore.FindAsync(session, id, cancellationToken))!;
                await session.CommitAsync(cancellationToken);
                LogSaved(logger, user.Id, id, result.Status);
                return Results.Ok(updated);
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // The same test arrived twice at the same moment and the other one won: look again.
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden");
            }
        }

        return ApiResults.Error(StatusCodes.Status409Conflict, "conflict");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Test result {ResultId} saved as {Status} by {UserId}")]
    private static partial void LogSaved(ILogger logger, string userId, Guid resultId, string status);
}

/// <summary>GET /api/results?updatedSince=: tests saved or changed after a moment.</summary>
public sealed class GetTestResults : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/results", Handle)
            .WithName("GetTestResults")
            .WithTags("Results")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<List<TestResultDto>>(StatusCodes.Status200OK);

    public static Task<IResult> Handle(
        ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken, [FromQuery] DateTimeOffset? updatedSince = null) =>
        Pull.RunAsync(principal, sessions, (session, ct) => TestResultStore.PullAsync(session, updatedSince, Pull.Limit, ct), cancellationToken);
}

/// <summary>Reads and writes test results.</summary>
internal static class TestResultStore
{
    private const string Select = """
        SELECT t.id, p.fhir_id AS patient_id, t.category, t.test, t.status, t.facility, t.result, t.flag, t.notes,
               t.requested_at, t.resulted_at, t.created_by, t.created_at, t.updated_at, t.version
        FROM test_results t JOIN patients p ON p.id = t.patient_id
        """;

    public static async Task<TestResultDto?> FindAsync(RoleSession session, Guid id, CancellationToken cancellationToken) =>
        (await session.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Select + " WHERE t.id = @id", new { id }, session.Transaction, cancellationToken: cancellationToken)))?.ToDto();

    public static async Task<List<TestResultDto>> PullAsync(RoleSession session, DateTimeOffset? since, int limit, CancellationToken cancellationToken) =>
        [.. (await session.Connection.QueryAsync<Row>(new CommandDefinition(
            Select + " WHERE (@since::timestamptz IS NULL OR t.updated_at > @since) ORDER BY t.updated_at, t.id LIMIT @limit",
            new { since = since?.ToUniversalTime(), limit }, session.Transaction, cancellationToken: cancellationToken))).Select(r => r.ToDto())];

    public static Task InsertAsync(RoleSession session, Guid id, int patientInternalId, TestResultDto r, CancellationToken cancellationToken) =>
        session.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO test_results (id, patient_id, category, test, status, facility, result, flag, notes, requested_at, resulted_at, created_by, version)
            VALUES (@id, @patientInternalId, @category, @test, @status, @facility, @result, @flag, @notes, @requestedAt, @resultedAt, @by, 1)
            """,
            new
            {
                id,
                patientInternalId,
                category = r.Category,
                test = r.Test.Trim(),
                status = r.Status,
                facility = Blank(r.Facility),
                result = Blank(r.Result),
                flag = r.Flag,
                notes = Blank(r.Notes),
                requestedAt = r.RequestedAt.ToUniversalTime(),
                resultedAt = r.ResultedAt?.ToUniversalTime(),
                by = session.User.Id,
            },
            session.Transaction, cancellationToken: cancellationToken));

    public static async Task<bool> UpdateAsync(RoleSession session, Guid id, int expectedVersion, TestResultDto r, CancellationToken cancellationToken) =>
        await session.Connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE test_results SET status = @status, facility = @facility, result = @result, flag = @flag, notes = @notes,
                   resulted_at = @resultedAt, updated_by = @by, updated_at = now(), version = version + 1
            WHERE id = @id AND version = @expectedVersion
            """,
            new
            {
                id,
                expectedVersion,
                status = r.Status,
                facility = Blank(r.Facility),
                result = Blank(r.Result),
                flag = r.Flag,
                notes = Blank(r.Notes),
                resultedAt = r.ResultedAt?.ToUniversalTime(),
                by = session.User.Id,
            },
            session.Transaction, cancellationToken: cancellationToken)) == 1;

    /// <summary>True when two copies say the same thing (not the version, who saved it or the stored times).</summary>
    public static bool SameContent(TestResultDto a, TestResultDto b) =>
        a.PatientId == b.PatientId && a.Category == b.Category && a.Test.Trim() == b.Test.Trim() && a.Status == b.Status
        && Blank(a.Facility) == Blank(b.Facility) && Blank(a.Result) == Blank(b.Result) && a.Flag == b.Flag && Blank(a.Notes) == Blank(b.Notes)
        && a.ResultedAt?.ToUniversalTime() == b.ResultedAt?.ToUniversalTime();

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class Row
    {
        public Guid Id { get; set; }

        public Guid PatientId { get; set; }

        public string Category { get; set; } = string.Empty;

        public string Test { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? Facility { get; set; }

        public string? Result { get; set; }

        public string? Flag { get; set; }

        public string? Notes { get; set; }

        public DateTime RequestedAt { get; set; }

        public DateTime? ResultedAt { get; set; }

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int Version { get; set; }

        public TestResultDto ToDto() => new(
            Id.ToString(), PatientId.ToString(), Category, Test, Status, Facility, Result, Flag, Notes,
            Utc(RequestedAt), ResultedAt is { } at ? Utc(at) : null, CreatedBy, Utc(CreatedAt), Utc(UpdatedAt), Version);
    }
}
