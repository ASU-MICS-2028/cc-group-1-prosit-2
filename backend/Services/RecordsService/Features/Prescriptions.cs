using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Events;
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
/// PUT /api/prescriptions/{id}: medicines a doctor prescribes, and later that a pharmacy gave them out.
/// <list type="bullet">
/// <item>Only a doctor writes or cancels a prescription. Nurses and doctors share a database role, so this is checked here.</item>
/// <item>Any clinical worker may record that it was dispensed (the CHV who sees the patient back home, the nurse at the health centre),
/// naming the pharmacy. Who recorded it and when come from the sign-in and the clock.</item>
/// <item>The medicines never change once saved: a wrong prescription is cancelled and a new one written. That, the code tied to the
/// id, the doctor's name from the sign-in and the audit trail are the fraud checks.</item>
/// <item>A prescription is given out once: active can become dispensed or cancelled, and nothing after that.</item>
/// </list>
/// </summary>
public sealed partial class PutPrescription : IFeature
{
    public const int MaxItems = 10;
    public static readonly string[] Statuses = ["active", "dispensed", "cancelled"];

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/prescriptions/{id:guid}", Handle)
            .WithName("PutPrescription")
            .WithTags("Prescriptions")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Accepts<PrescriptionDto>("application/json")
            .Produces<PrescriptionDto>(StatusCodes.Status200OK)
            .Produces<PrescriptionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    /// <summary>The code a pharmacy reads: the first 8 hex digits of the id, so the phone can show it before the server has the record.</summary>
    public static string CodeFor(Guid id)
    {
        var hex = id.ToString("N")[..8].ToUpperInvariant();
        return $"RX-{hex[..4]}-{hex[4..]}";
    }

    [GeneratedRegex(@"^RX-[0-9A-F]{4}-[0-9A-F]{4}$")]
    private static partial Regex CodeShape();

    public static Dictionary<string, string> Check(Guid id, PrescriptionDto r, DateTimeOffset now)
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

        if (r.AppointmentId is not null && !Guid.TryParse(r.AppointmentId, out _))
        {
            errors["appointmentId"] = "Must be a UUID.";
        }

        if (r.Code is null || !CodeShape().IsMatch(r.Code) || r.Code != CodeFor(id))
        {
            errors["code"] = "Must be the code made from the id (RX-XXXX-XXXX).";
        }

        if (r.Items is null || r.Items.Count is 0 or > MaxItems)
        {
            errors["items"] = $"Between 1 and {MaxItems} medicines.";
        }
        else
        {
            for (var i = 0; i < r.Items.Count; i++)
            {
                var item = r.Items[i];
                var at = $"items[{i}]";
                if (string.IsNullOrWhiteSpace(item.Medicine) || item.Medicine.Trim().Length > 120)
                {
                    errors[$"{at}.medicine"] = "Required, at most 120 characters.";
                }

                if (string.IsNullOrWhiteSpace(item.Dose) || item.Dose.Trim().Length > 60)
                {
                    errors[$"{at}.dose"] = "Required, at most 60 characters.";
                }

                if (string.IsNullOrWhiteSpace(item.Frequency) || item.Frequency.Trim().Length > 60)
                {
                    errors[$"{at}.frequency"] = "Required, at most 60 characters.";
                }

                if (item.Days is < 1 or > 365)
                {
                    errors[$"{at}.days"] = "Between 1 and 365 days.";
                }

                if (item.Instructions is { Length: > 300 })
                {
                    errors[$"{at}.instructions"] = "At most 300 characters.";
                }
            }
        }

        if (r.Notes is { Length: > 2000 })
        {
            errors["notes"] = "At most 2000 characters.";
        }

        if (!Statuses.Contains(r.Status))
        {
            errors["status"] = "Must be active, dispensed or cancelled.";
        }

        if (r.Status == "dispensed" && string.IsNullOrWhiteSpace(r.Pharmacy))
        {
            errors["pharmacy"] = "Say which pharmacy or health centre gave it out.";
        }

        if (r.Pharmacy is { Length: > 200 })
        {
            errors["pharmacy"] = "At most 200 characters.";
        }

        if (r.PrescribedAt > now.AddDays(1) || r.PrescribedAt < now.AddYears(-5))
        {
            errors["prescribedAt"] = "Must be a believable time.";
        }

        return errors;
    }

    public static async Task<IResult> Handle(
        Guid id,
        PrescriptionDto prescription,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        TimeProvider time,
        ILogger<PutPrescription> logger,
        IAppointmentEvents events,
        AppointmentEventOptions eventOptions,
        CancellationToken cancellationToken)
    {
        var errors = Check(id, prescription, time.GetUtcNow());
        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var isDoctor = user.Role == Roles.Doctor;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var session = await sessions.TryBeginAsync(user, cancellationToken);
            if (session is null)
            {
                return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
            }

            try
            {
                var existing = await PrescriptionStore.FindAsync(session, id, cancellationToken);
                if (existing is null)
                {
                    if (!isDoctor)
                    {
                        return ApiResults.Error(StatusCodes.Status403Forbidden, "only_doctors_prescribe");
                    }

                    if (prescription.Status != "active")
                    {
                        return ApiResults.Validation("status", "A new prescription is active.");
                    }

                    var patientInternalId = await session.Connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                        "SELECT id FROM patients WHERE fhir_id = @patient", new { patient = Guid.Parse(prescription.PatientId) },
                        session.Transaction, cancellationToken: cancellationToken));
                    if (patientInternalId is null)
                    {
                        return ApiResults.Validation("patientId", "This patient is not on the server yet.");
                    }

                    await PrescriptionStore.InsertAsync(session, id, patientInternalId.Value, prescription, cancellationToken);
                    var created = (await PrescriptionStore.FindAsync(session, id, cancellationToken))!;
                    var contact = await session.Connection.QuerySingleAsync<(string? Phone, string? Language)>(new CommandDefinition(
                        "SELECT phone, preferred_language FROM patients WHERE id = @id", new { id = patientInternalId.Value },
                        session.Transaction, cancellationToken: cancellationToken));
                    await session.CommitAsync(cancellationToken);
                    LogSaved(logger, user.Id, id, "active");

                    // After the commit, and never failing the save: the patient gets the code by SMS (functions/src/appointment_reminder.py).
                    await events.PublishAsync(
                        CareEvent.ForPrescription(created, contact.Phone, contact.Language, eventOptions.Environment, time.GetUtcNow()), cancellationToken);
                    return Results.Json(created, statusCode: StatusCodes.Status201Created);
                }

                if (!PrescriptionStore.SameMedicines(existing, prescription))
                {
                    return ApiResults.Validation("items", "A saved prescription cannot change; cancel it and write a new one.");
                }

                if (existing.Version != prescription.Version)
                {
                    return existing.Status == prescription.Status && (existing.Pharmacy ?? string.Empty) == (prescription.Pharmacy?.Trim() ?? string.Empty)
                        ? Results.Ok(existing)
                        : ApiResults.Conflict(existing);
                }

                if (existing.Status == prescription.Status)
                {
                    return Results.Ok(existing);   // nothing to change
                }

                if (existing.Status != "active")
                {
                    return ApiResults.Conflict(existing);   // already given out or cancelled
                }

                if (prescription.Status == "cancelled" && !isDoctor)
                {
                    return ApiResults.Error(StatusCodes.Status403Forbidden, "only_doctors_cancel");
                }

                if (!await PrescriptionStore.MoveAsync(session, id, existing.Version, prescription, cancellationToken))
                {
                    return ApiResults.Conflict(existing);
                }

                var updated = (await PrescriptionStore.FindAsync(session, id, cancellationToken))!;
                await session.CommitAsync(cancellationToken);
                LogSaved(logger, user.Id, id, prescription.Status);
                return Results.Ok(updated);
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // The same prescription arrived twice at the same moment and the other one won: look again.
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden");
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                return ApiResults.Validation("appointmentId", "This visit is not on the server yet.");
            }
        }

        return ApiResults.Error(StatusCodes.Status409Conflict, "conflict");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Prescription {PrescriptionId} saved as {Status} by {UserId}")]
    private static partial void LogSaved(ILogger logger, string userId, Guid prescriptionId, string status);
}

/// <summary>GET /api/prescriptions?updatedSince=: prescriptions written or changed after a moment.</summary>
public sealed class GetPrescriptions : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/prescriptions", Handle)
            .WithName("GetPrescriptions")
            .WithTags("Prescriptions")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<List<PrescriptionDto>>(StatusCodes.Status200OK);

    public static Task<IResult> Handle(
        ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken, [FromQuery] DateTimeOffset? updatedSince = null) =>
        Pull.RunAsync(principal, sessions, (session, ct) => PrescriptionStore.PullAsync(session, updatedSince, Pull.Limit, ct), cancellationToken);
}

/// <summary>Reads and writes prescriptions. The medicines are one JSON array.</summary>
internal static class PrescriptionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Select = """
        SELECT r.id, p.fhir_id AS patient_id, r.appointment_id, r.code, r.items::text AS items, r.notes, r.status, r.prescribed_by,
               r.prescriber, r.prescribed_at, r.dispensed_by, r.dispensed_at, r.pharmacy, r.created_at, r.updated_at, r.version
        FROM prescriptions r JOIN patients p ON p.id = r.patient_id
        """;

    public static async Task<PrescriptionDto?> FindAsync(RoleSession session, Guid id, CancellationToken cancellationToken) =>
        (await session.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Select + " WHERE r.id = @id", new { id }, session.Transaction, cancellationToken: cancellationToken)))?.ToDto();

    public static async Task<List<PrescriptionDto>> PullAsync(RoleSession session, DateTimeOffset? since, int limit, CancellationToken cancellationToken) =>
        [.. (await session.Connection.QueryAsync<Row>(new CommandDefinition(
            Select + " WHERE (@since::timestamptz IS NULL OR r.updated_at > @since) ORDER BY r.updated_at, r.id LIMIT @limit",
            new { since = since?.ToUniversalTime(), limit }, session.Transaction, cancellationToken: cancellationToken))).Select(r => r.ToDto())];

    public static Task InsertAsync(RoleSession session, Guid id, int patientInternalId, PrescriptionDto r, CancellationToken cancellationToken) =>
        session.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO prescriptions (id, patient_id, appointment_id, code, items, notes, status, prescribed_by, prescriber, prescribed_at, version)
            VALUES (@id, @patientInternalId, @appointmentId, @code, CAST(@items AS jsonb), @notes, 'active', @by, @name, @prescribedAt, 1)
            """,
            new
            {
                id,
                patientInternalId,
                appointmentId = r.AppointmentId is null ? (Guid?)null : Guid.Parse(r.AppointmentId),
                code = r.Code,
                items = JsonSerializer.Serialize(Normalise(r.Items), Json),
                notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
                by = session.User.Id,
                name = string.IsNullOrWhiteSpace(session.User.Name) ? session.User.Id : session.User.Name,
                prescribedAt = r.PrescribedAt.ToUniversalTime(),
            },
            session.Transaction, cancellationToken: cancellationToken));

    /// <summary>Dispensed (with the pharmacy, who recorded it and now) or cancelled, only from the version the caller saw.</summary>
    public static async Task<bool> MoveAsync(RoleSession session, Guid id, int expectedVersion, PrescriptionDto r, CancellationToken cancellationToken) =>
        await session.Connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE prescriptions SET status = @status,
                   pharmacy = CASE WHEN @status = 'dispensed' THEN @pharmacy ELSE pharmacy END,
                   dispensed_by = CASE WHEN @status = 'dispensed' THEN @by ELSE dispensed_by END,
                   dispensed_at = CASE WHEN @status = 'dispensed' THEN now() ELSE dispensed_at END,
                   updated_at = now(), version = version + 1
            WHERE id = @id AND version = @expectedVersion AND status = 'active'
            """,
            new { id, expectedVersion, status = r.Status, pharmacy = r.Pharmacy?.Trim(), by = session.User.Id },
            session.Transaction, cancellationToken: cancellationToken)) == 1;

    /// <summary>True when both list the same medicines for the same patient (a prescription's medicines never change).</summary>
    public static bool SameMedicines(PrescriptionDto saved, PrescriptionDto incoming) =>
        saved.PatientId == incoming.PatientId && saved.Code == incoming.Code
        && JsonSerializer.Serialize(Normalise(saved.Items), Json) == JsonSerializer.Serialize(Normalise(incoming.Items), Json);

    private static List<PrescriptionItemDto> Normalise(List<PrescriptionItemDto> items) =>
        [.. items.Select(i => new PrescriptionItemDto(
            i.Medicine.Trim(), i.Dose.Trim(), i.Frequency.Trim(), i.Days, string.IsNullOrWhiteSpace(i.Instructions) ? null : i.Instructions.Trim()))];

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class Row
    {
        public Guid Id { get; set; }

        public Guid PatientId { get; set; }

        public Guid? AppointmentId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Items { get; set; } = "[]";

        public string? Notes { get; set; }

        public string Status { get; set; } = string.Empty;

        public string PrescribedBy { get; set; } = string.Empty;

        public string Prescriber { get; set; } = string.Empty;

        public DateTime PrescribedAt { get; set; }

        public string? DispensedBy { get; set; }

        public DateTime? DispensedAt { get; set; }

        public string? Pharmacy { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int Version { get; set; }

        public PrescriptionDto ToDto() => new(
            Id.ToString(), PatientId.ToString(), AppointmentId?.ToString(), Code,
            JsonSerializer.Deserialize<List<PrescriptionItemDto>>(Items, Json) ?? [], Notes, Status, PrescribedBy, Prescriber,
            Utc(PrescribedAt), DispensedBy, DispensedAt is { } at ? Utc(at) : null, Pharmacy, Utc(CreatedAt), Utc(UpdatedAt), Version);
    }
}
