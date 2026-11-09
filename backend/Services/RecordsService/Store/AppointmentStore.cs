using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;

namespace HealthConnect.RecordsService.Store;

/// <summary>An appointment as stored, with the table's number for its patient (which never leaves the server).</summary>
internal sealed record StoredAppointment(int PatientInternalId, AppointmentDto Dto);

/// <summary>Reads and writes visits, referrals and teleconsults.</summary>
internal static class AppointmentStore
{
    private const string SelectAppointments = """
        SELECT a.id, a.patient_id AS patient_internal_id, p.fhir_id AS patient_id, a.type, a.scheduled_at, a.facility, a.reason,
               a.priority, a.status, a.referral_status, a.handled_by, a.handled_at, a.created_at, a.updated_at, a.version
        FROM appointments a JOIN patients p ON p.id = a.patient_id
        """;

    public static async Task<StoredAppointment?> FindAsync(RoleSession session, Guid id, CancellationToken cancellationToken)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<AppointmentRow>(new CommandDefinition(
            SelectAppointments + " WHERE a.id = @id", new { id }, session.Transaction, cancellationToken: cancellationToken));
        return row?.ToStored();
    }

    /// <summary>Appointments changed after a moment, oldest change first, for another worker's phone to pull (the referral inbox lives on this).</summary>
    public static async Task<List<AppointmentDto>> PullAsync(RoleSession session, DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        var rows = await session.Connection.QueryAsync<AppointmentRow>(new CommandDefinition(
            SelectAppointments + " WHERE (@since::timestamptz IS NULL OR a.updated_at > @since) ORDER BY a.updated_at, a.id LIMIT @limit",
            new { since = since?.ToUniversalTime(), limit }, session.Transaction, cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToStored().Dto)];
    }

    public static Task InsertAsync(
        RoleSession session, Guid id, int patientInternalId, AppointmentDto appointment, string? referralStatus, CancellationToken cancellationToken) =>
        session.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO appointments (id, patient_id, type, scheduled_at, facility, reason, priority, status, referral_status, created_by, version)
            VALUES (@id, @patientInternalId, @type, @scheduledAt, @facility, @reason, @priority, @status, @referralStatus, @by, 1)
            """,
            new
            {
                id,
                patientInternalId,
                type = appointment.Type,
                scheduledAt = appointment.ScheduledAt.ToUniversalTime(),
                facility = appointment.Facility.Trim(),
                reason = appointment.Reason ?? string.Empty,
                priority = appointment.Priority,
                status = appointment.Status,
                referralStatus,
                by = session.User.Id,
            },
            session.Transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// Changes an appointment only if it is still at the version the caller saw. When the referral status moves, who handled it
    /// and when come from the signed-in worker and the clock, never from the request.
    /// </summary>
    public static async Task<bool> UpdateAsync(
        RoleSession session,
        Guid id,
        int expectedVersion,
        AppointmentDto appointment,
        string? referralStatus,
        bool referralStatusChanged,
        CancellationToken cancellationToken)
    {
        var rows = await session.Connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE appointments SET scheduled_at = @scheduledAt, facility = @facility, reason = @reason, priority = @priority, status = @status,
                   referral_status = @referralStatus,
                   handled_by = CASE WHEN @changed THEN @by ELSE handled_by END,
                   handled_at = CASE WHEN @changed THEN now() ELSE handled_at END,
                   version = version + 1, updated_at = now()
            WHERE id = @id AND version = @expectedVersion
            """,
            new
            {
                id,
                expectedVersion,
                scheduledAt = appointment.ScheduledAt.ToUniversalTime(),
                facility = appointment.Facility.Trim(),
                reason = appointment.Reason ?? string.Empty,
                priority = appointment.Priority,
                status = appointment.Status,
                referralStatus,
                changed = referralStatusChanged && referralStatus != "requested",
                by = session.User.Id,
            },
            session.Transaction, cancellationToken: cancellationToken));
        return rows == 1;
    }

    /// <summary>True when two appointments say the same thing (not the id, version, the stored times or who handled it).</summary>
    public static bool SameContent(AppointmentDto a, AppointmentDto b) =>
        a.PatientId == b.PatientId && a.Type == b.Type && a.ScheduledAt.ToUniversalTime() == b.ScheduledAt.ToUniversalTime()
        && a.Facility.Trim() == b.Facility.Trim() && (a.Reason ?? string.Empty) == (b.Reason ?? string.Empty)
        && a.Priority == b.Priority && a.Status == b.Status && a.ReferralStatus == b.ReferralStatus;

    private sealed class AppointmentRow
    {
        public Guid Id { get; set; }

        public int PatientInternalId { get; set; }

        public Guid PatientId { get; set; }

        public string Type { get; set; } = string.Empty;

        public DateTime ScheduledAt { get; set; }

        public string Facility { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public string Priority { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? ReferralStatus { get; set; }

        public string? HandledBy { get; set; }

        public DateTime? HandledAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int Version { get; set; }

        public StoredAppointment ToStored() => new(PatientInternalId, new AppointmentDto(
            Id: Id.ToString(),
            PatientId: PatientId.ToString(),
            Type: Type,
            ScheduledAt: Utc(ScheduledAt),
            Facility: Facility,
            Reason: Reason,
            Priority: Priority,
            Status: Status,
            ReferralStatus: ReferralStatus,
            HandledBy: HandledBy,
            HandledAt: HandledAt is null ? null : Utc(HandledAt.Value),
            CreatedAt: Utc(CreatedAt),
            UpdatedAt: Utc(UpdatedAt),
            Version: Version));

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
