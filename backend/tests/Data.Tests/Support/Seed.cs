namespace HealthConnect.Data.Tests.Support;

/// <summary>Small helpers that add synthetic rows. All names are invented; nothing here is a real person.</summary>
public static class Seed
{
    public const string Shai = "Shai-Osudoku";
    public const string Kintampo = "Kintampo North";

    public static Task<int> AddPatientAsync(this DbSession session, string district = Shai, string name = "Synthetic Patient") =>
        session.ScalarAsync<int>(
            "INSERT INTO patients (full_name, gender, district) VALUES (@name, 'female', @district) RETURNING id",
            ("name", name), ("district", district));

    /// <summary>One measurement. Blood pressure is two calls (systolic, diastolic).</summary>
    public static Task AddReadingAsync(
        this DbSession session, int patientId, string type, string value, DateTimeOffset at, string status = "final")
    {
        var (loinc, unit) = type switch
        {
            "systolic_bp" => ("8480-6", "mm[Hg]"),
            "diastolic_bp" => ("8462-4", "mm[Hg]"),
            "body_temperature" => ("8310-5", "Cel"),
            "heart_rate" => ("8867-4", "/min"),
            "body_weight" => ("29463-7", "kg"),
            "body_height" => ("8302-2", "cm"),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        return session.ExecAsync(
            """
            INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, recorded_by, recorded_at, status)
            VALUES (@patient, @type, @loinc, @value, @unit, 'chv-test', @at, @status)
            """,
            ("patient", patientId), ("type", type), ("loinc", loinc), ("value", value), ("unit", unit), ("at", at), ("status", status));
    }

    /// <summary>A patient in a district with one reading on the given day.</summary>
    public static async Task<int> AddPatientWithReadingAsync(
        this DbSession session, string district, DateTimeOffset at, string? systolic = "118", string? diastolic = "76", string? temperature = "36.8",
        string status = "final")
    {
        var id = await session.AddPatientAsync(district);
        if (systolic is not null)
        {
            await session.AddReadingAsync(id, "systolic_bp", systolic, at, status);
        }

        if (diastolic is not null)
        {
            await session.AddReadingAsync(id, "diastolic_bp", diastolic, at, status);
        }

        if (temperature is not null)
        {
            await session.AddReadingAsync(id, "body_temperature", temperature, at, status);
        }

        return id;
    }

    public static Task<Guid> AddVitalsAsync(this DbSession session, int patientId) =>
        session.ScalarAsync<Guid>(
            "INSERT INTO vitals (id, patient_id, taken_at, recorded_by) VALUES (gen_random_uuid(), @p, now(), 'chv-test') RETURNING id",
            ("p", patientId));

    public static Task<Guid> AddAppointmentAsync(
        this DbSession session, int patientId, string type = "follow_up", string? referralStatus = null) =>
        session.ScalarAsync<Guid>(
            """
            INSERT INTO appointments (id, patient_id, type, scheduled_at, facility, referral_status)
            VALUES (gen_random_uuid(), @p, @type, now() + interval '1 day', 'Dodowa District Hospital', @rs) RETURNING id
            """,
            ("p", patientId), ("type", type), ("rs", referralStatus));

    public static Task<Guid> AddMediaAsync(this DbSession session, int patientId, string? key = null)
    {
        var id = Guid.NewGuid();
        return session.ScalarAsync<Guid>(
            """
            INSERT INTO media (id, patient_id, owner_entity, owner_id, kind, mime, size_bytes, sha256, s3_key, requested_by)
            VALUES (@id, @p, 'patient', @p_owner, 'consent-audio', 'audio/webm', 61000, @sha, @key, 'chv-test') RETURNING id
            """,
            ("id", id), ("p", patientId), ("p_owner", Guid.NewGuid()), ("sha", new string('a', 64)), ("key", key ?? $"media/test/{id}"));
    }

    public static Task AddAuditEntryAsync(this DbSession session, string by = "chv-test", int? patientId = null) =>
        session.ExecAsync(
            "INSERT INTO access_log (accessed_by, role, patient_id, action) VALUES (@by, 'chv', @p, 'read')",
            ("by", by), ("p", patientId));
}
