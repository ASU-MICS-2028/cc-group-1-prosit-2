namespace HealthConnect.RecordsService.Models;

/// <summary>The proof of consent: a recording, signature or thumbprint photo (uploaded separately; the hash proves it was not changed).</summary>
public sealed record EvidenceDto(string MediaId, string Sha256, string Mime);

/// <summary>Consent comes before any health data is saved (Act 843 s.20 and s.37): how it was given, when, in which wording and language.</summary>
public sealed record ConsentDto(
    bool Given,
    string Method,
    DateTimeOffset At,
    string? StatementVersion,
    string? Language,
    string? ObtainedBy,
    string? Witness,
    EvidenceDto? Evidence);

/// <summary>A patient, exactly as the app keeps it (frontend/src/db/types.ts). The id is made on the phone.</summary>
public sealed record PatientDto(
    string Id,
    string GivenName,
    string FamilyName,
    string Sex,
    DateOnly DateOfBirth,
    bool BirthDateEstimated,
    string? Phone,
    string? NationalId,
    string Community,
    string District,
    string PreferredLanguage,
    ConsentDto Consent,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    int Version);

/// <summary>One set of measurements taken at one time. At least one value; blood pressure comes as a pair.</summary>
public sealed record VitalsDto(
    string Id,
    string PatientId,
    DateTimeOffset TakenAt,
    int? Systolic,
    int? Diastolic,
    decimal? TemperatureC,
    decimal? WeightKg,
    decimal? HeightCm,
    int? Pulse,
    string? Notes,
    DateTimeOffset? CreatedAt,
    int Version);

/// <summary>A visit, a referral or a teleconsult booking. Referrals and teleconsults also carry where they stand (requested, accepted, declined, seen).</summary>
public sealed record AppointmentDto(
    string Id,
    string PatientId,
    string Type,
    DateTimeOffset ScheduledAt,
    string Facility,
    string? Reason,
    string Priority,
    string Status,
    string? ReferralStatus,
    string? HandledBy,
    DateTimeOffset? HandledAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    int Version);

/// <summary>One access event as the phone records it (frontend/src/db/types.ts AuditEvent). Who did it comes from the sign-in, not from this.</summary>
public sealed record AuditEventDto(
    string Id,
    DateTimeOffset At,
    string Action,
    string Entity,
    string? EntityId,
    string? PatientId,
    string Outcome,
    string Purpose,
    string? Detail);

/// <summary>How many events arrived and how many were new (the rest had been uploaded before).</summary>
public sealed record AuditUploadResult(int Received, int Saved);

/// <summary>One entry of the server's audit log, as the auditor reads it.</summary>
public sealed record AuditEntryDto(
    int Id,
    string ActorId,
    string? Role,
    string Action,
    string Outcome,
    string Purpose,
    string? Entity,
    string? EntityId,
    string? PatientId,
    string? Detail,
    string? SourceIp,
    DateTimeOffset RecordedAt,
    DateTimeOffset? HappenedAt);

/// <summary>Whether the log's hash chain still checks out; if not, the first entry that does not.</summary>
public sealed record AuditVerification(bool Intact, long EntriesChecked, int? FirstBadId);

/// <summary>Asks for a link to upload one file: what it is, how big, its SHA-256 (the proof it was not changed), and the patient and record it belongs to.</summary>
public sealed record MediaUploadRequest(
    string Kind,
    string Mime,
    int Size,
    string Sha256,
    string PatientId,
    string OwnerEntity,
    string OwnerId);

/// <summary>The link and what the phone must send with it. The link stops working at expiresAt.</summary>
public sealed record MediaUploadResponse(string Url, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt, string Key);

