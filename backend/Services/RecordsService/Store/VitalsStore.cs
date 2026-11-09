using System.Globalization;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Validation;

namespace HealthConnect.RecordsService.Store;

/// <summary>Reads and writes vitals. One reading is a row in vitals plus one observations row per measurement (coded in LOINC), as the schema's FHIR shape asks.</summary>
internal static class VitalsStore
{
    public static async Task<VitalsDto?> FindAsync(RoleSession session, Guid id, CancellationToken cancellationToken)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<VitalsRow>(new CommandDefinition(
            SelectVitals + " WHERE v.id = @id", new { id }, session.Transaction, cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var measurements = await session.Connection.QueryAsync<MeasurementRow>(new CommandDefinition(
            "SELECT vitals_id, observation_type, value FROM observations WHERE vitals_id = @id AND status <> 'entered-in-error'",
            new { id }, session.Transaction, cancellationToken: cancellationToken));
        return ToDto(row, measurements.ToLookup(m => m.VitalsId)[row.Id]);
    }

    /// <summary>Readings stored after a moment, oldest first, for another worker's phone to pull.</summary>
    public static async Task<List<VitalsDto>> PullAsync(RoleSession session, DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        var rows = (await session.Connection.QueryAsync<VitalsRow>(new CommandDefinition(
            SelectVitals + " WHERE (@since::timestamptz IS NULL OR v.created_at > @since) ORDER BY v.created_at, v.id LIMIT @limit",
            new { since = since?.ToUniversalTime(), limit }, session.Transaction, cancellationToken: cancellationToken))).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToArray();
        var measurements = (await session.Connection.QueryAsync<MeasurementRow>(new CommandDefinition(
            "SELECT vitals_id, observation_type, value FROM observations WHERE vitals_id = ANY(@ids) AND status <> 'entered-in-error'",
            new { ids }, session.Transaction, cancellationToken: cancellationToken))).ToLookup(m => m.VitalsId);
        return [.. rows.Select(r => ToDto(r, measurements[r.Id]))];
    }

    public static async Task InsertAsync(
        RoleSession session, Guid id, int patientInternalId, VitalsDto vitals, CancellationToken cancellationToken)
    {
        await session.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO vitals (id, patient_id, taken_at, notes, recorded_by, version) VALUES (@id, @patientInternalId, @takenAt, @notes, @by, 1)",
            new
            {
                id,
                patientInternalId,
                takenAt = vitals.TakenAt.ToUniversalTime(),
                notes = string.IsNullOrWhiteSpace(vitals.Notes) ? null : vitals.Notes,
                by = session.User.Id,
            },
            session.Transaction, cancellationToken: cancellationToken));

        var panel = Guid.NewGuid();   // systolic and diastolic belong together as one blood-pressure panel (LOINC 85354-9)
        foreach (var (range, value) in VitalsValidator.Values(vitals))
        {
            if (value is null)
            {
                continue;
            }

            await session.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO observations (patient_id, vitals_id, observation_type, loinc_code, value, unit, panel_id, recorded_by, recorded_at)
                SELECT @patientInternalId, @id, c.short_name, c.loinc_code, @value, c.ucum_unit, @panel, @by, @takenAt
                FROM observation_codes c WHERE c.short_name = @type
                """,
                new
                {
                    patientInternalId,
                    id,
                    value = value.Value.ToString("0.##", CultureInfo.InvariantCulture),
                    panel = range.Type is "systolic_bp" or "diastolic_bp" ? panel : (Guid?)null,
                    by = session.User.Id,
                    takenAt = vitals.TakenAt.ToUniversalTime(),
                    type = range.Type,
                },
                session.Transaction, cancellationToken: cancellationToken));
        }
    }

    /// <summary>True when two readings say the same thing (the numbers, the time, the note and the patient; not the id, version or stored time).</summary>
    public static bool SameContent(VitalsDto a, VitalsDto b) =>
        a.PatientId == b.PatientId && a.TakenAt.ToUniversalTime() == b.TakenAt.ToUniversalTime()
        && (a.Notes ?? string.Empty) == (b.Notes ?? string.Empty)
        && VitalsValidator.Values(a).Select(v => v.Value).SequenceEqual(VitalsValidator.Values(b).Select(v => v.Value));

    private const string SelectVitals = """
        SELECT v.id, p.fhir_id AS patient_id, v.taken_at, v.notes, v.version, v.created_at
        FROM vitals v JOIN patients p ON p.id = v.patient_id
        """;

    private static VitalsDto ToDto(VitalsRow row, IEnumerable<MeasurementRow> measurements)
    {
        var values = measurements.ToDictionary(m => m.ObservationType, m => decimal.Parse(m.Value, CultureInfo.InvariantCulture));
        decimal? Get(string type) => values.TryGetValue(type, out var value) ? value : null;

        return new VitalsDto(
            Id: row.Id.ToString(),
            PatientId: row.PatientId.ToString(),
            TakenAt: new DateTimeOffset(DateTime.SpecifyKind(row.TakenAt, DateTimeKind.Utc)),
            Systolic: (int?)Get("systolic_bp"),
            Diastolic: (int?)Get("diastolic_bp"),
            TemperatureC: Get("body_temperature"),
            WeightKg: Get("body_weight"),
            HeightCm: Get("body_height"),
            Pulse: (int?)Get("heart_rate"),
            Notes: row.Notes,
            CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
            Version: row.Version);
    }

    private sealed class VitalsRow
    {
        public Guid Id { get; set; }

        public Guid PatientId { get; set; }

        public DateTime TakenAt { get; set; }

        public string? Notes { get; set; }

        public int Version { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private sealed class MeasurementRow
    {
        public Guid VitalsId { get; set; }

        public string ObservationType { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}
