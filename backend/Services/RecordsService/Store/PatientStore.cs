using System.Globalization;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;

namespace HealthConnect.RecordsService.Store;

/// <summary>A patient as stored: the app's record, plus the table's own number (which never leaves the server).</summary>
internal sealed record StoredPatient(int InternalId, PatientDto Dto);

/// <summary>Reads and writes patients, their consent history and their national id. Every call runs as the caller's database role, so row-level security applies.</summary>
internal static class PatientStore
{
    /// <summary>The system a patient's national id (Ghana Card or NHIS number) is filed under in patient_identifiers.</summary>
    public const string NationalIdSystem = "urn:healthconnect-gh:national-id";

    private const string SelectPatients = """
        SELECT p.id AS internal_id, p.fhir_id, p.given_name, p.family_name, p.full_name, p.gender,
               to_char(coalesce(p.date_of_birth, DATE '1900-01-01'), 'YYYY-MM-DD') AS date_of_birth,
               p.birth_date_estimated, p.phone, p.community, p.district, p.preferred_language, p.version,
               p.created_at, p.updated_at, p.consent_given_at, p.consent_method,
               c.statement_version, c.language AS consent_language, c.witness,
               c.evidence_media_id, c.evidence_sha256, c.evidence_mime,
               i.value AS national_id
        FROM patients p
        LEFT JOIN LATERAL (
            SELECT * FROM consents c WHERE c.patient_id = p.id AND c.status = 'active' ORDER BY c.id DESC LIMIT 1) c ON true
        LEFT JOIN patient_identifiers i ON i.patient_id = p.id AND i.system = 'urn:healthconnect-gh:national-id' AND i.use <> 'secondary'
        """;

    public static async Task<StoredPatient?> FindAsync(RoleSession session, Guid fhirId, CancellationToken cancellationToken)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<PatientRow>(new CommandDefinition(
            SelectPatients + " WHERE p.fhir_id = @id", new { id = fhirId }, session.Transaction, cancellationToken: cancellationToken));
        return row is null ? null : row.ToStored();
    }

    /// <summary>Patients changed after a moment, oldest change first, for another worker's phone to pull.</summary>
    public static async Task<List<PatientDto>> PullAsync(RoleSession session, DateTimeOffset? updatedSince, int limit, CancellationToken cancellationToken)
    {
        var rows = await session.Connection.QueryAsync<PatientRow>(new CommandDefinition(
            SelectPatients + " WHERE (@since::timestamptz IS NULL OR p.updated_at > @since) ORDER BY p.updated_at, p.id LIMIT @limit",
            new { since = updatedSince?.ToUniversalTime(), limit }, session.Transaction, cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToStored().Dto)];
    }

    public static async Task<int> InsertAsync(
        RoleSession session, Guid fhirId, PatientDto patient, string? phone, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var internalId = await session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO patients (fhir_id, full_name, given_name, family_name, gender, date_of_birth, birth_date_estimated, phone,
                                  community, district, preferred_language, consent_given_at, consent_method, version, created_by, updated_by, created_at)
            VALUES (@fhirId, @full, @given, @family, @sex, @dob, @estimated, @phone,
                    @community, @district, @language, @consentAt, @consentMethod, 1, @by, @by, @createdAt)
            RETURNING id
            """,
            Parameters(session, fhirId, patient, phone) with { CreatedAt = createdAt },
            session.Transaction, cancellationToken: cancellationToken));
        return internalId;
    }

    /// <summary>Changes a patient only if it is still at the version the caller saw. Returns false when someone else got there first.</summary>
    public static async Task<bool> UpdateAsync(
        RoleSession session, int internalId, int expectedVersion, PatientDto patient, string? phone, CancellationToken cancellationToken)
    {
        var rows = await session.Connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE patients SET full_name = @full, given_name = @given, family_name = @family, gender = @sex, date_of_birth = @dob,
                   birth_date_estimated = @estimated, phone = @phone, community = @community, district = @district,
                   preferred_language = @language, consent_given_at = @consentAt, consent_method = @consentMethod,
                   version = version + 1, updated_by = @by, updated_at = now()
            WHERE id = @internalId AND version = @expectedVersion
            """,
            Parameters(session, Guid.Empty, patient, phone) with { InternalId = internalId, ExpectedVersion = expectedVersion },
            session.Transaction, cancellationToken: cancellationToken));
        return rows == 1;
    }

    /// <summary>Adds a row to the consent history (nothing is ever overwritten: withdrawal and re-consent stay visible).</summary>
    public static Task AddConsentAsync(RoleSession session, int internalId, Guid fhirId, ConsentDto consent, CancellationToken cancellationToken) =>
        session.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO consents (patient_id, status, method, obtained_by, statement_version, language, witness,
                                  evidence_media_id, evidence_key, evidence_sha256, evidence_mime, recorded_at)
            VALUES (@internalId, 'active', @method, @by, @statementVersion, @language, @witness,
                    @mediaId, @key, @sha, @mime, @at)
            """,
            new
            {
                internalId,
                method = consent.Method,
                by = session.User.Id,
                statementVersion = consent.StatementVersion,
                language = consent.Language,
                witness = string.IsNullOrWhiteSpace(consent.Witness) ? null : consent.Witness,
                mediaId = consent.Evidence is null ? (Guid?)null : Guid.Parse(consent.Evidence.MediaId),
                key = consent.Evidence is null ? null : $"media/{fhirId}/{consent.Evidence.MediaId}",
                sha = consent.Evidence?.Sha256,
                mime = consent.Evidence?.Mime,
                at = consent.At.ToUniversalTime(),
            },
            session.Transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// Files the national id. If the patient already has a different one it is kept as "secondary" (nobody may delete). Returns false when
    /// the number already belongs to another patient (a duplicate registration).
    /// </summary>
    public static async Task<bool> SetNationalIdAsync(RoleSession session, int internalId, string nationalId, CancellationToken cancellationToken)
    {
        await session.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE patient_identifiers SET use = 'secondary' WHERE patient_id = @internalId AND system = @system AND value <> @value AND use <> 'secondary'",
            new { internalId, system = NationalIdSystem, value = nationalId }, session.Transaction, cancellationToken: cancellationToken));

        var owner = await session.Connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT patient_id FROM patient_identifiers WHERE system = @system AND value = @value",
            new { system = NationalIdSystem, value = nationalId }, session.Transaction, cancellationToken: cancellationToken));
        if (owner is not null && owner != internalId)
        {
            return false;
        }

        await session.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO patient_identifiers (patient_id, system, value, use) VALUES (@internalId, @system, @value, 'official')
            ON CONFLICT (system, value) DO UPDATE SET use = 'official' WHERE patient_identifiers.patient_id = @internalId
            """,
            new { internalId, system = NationalIdSystem, value = nationalId }, session.Transaction, cancellationToken: cancellationToken));
        return true;
    }

    /// <summary>True when two versions of a patient say the same thing (id, version and the two dates are not content).</summary>
    public static bool SameContent(PatientDto a, PatientDto b) =>
        a.GivenName == b.GivenName && a.FamilyName == b.FamilyName && a.Sex == b.Sex && a.DateOfBirth == b.DateOfBirth
        && a.BirthDateEstimated == b.BirthDateEstimated && a.Phone == b.Phone
        && (a.NationalId ?? string.Empty) == (b.NationalId ?? string.Empty)
        && a.Community == b.Community && a.District == b.District && a.PreferredLanguage == b.PreferredLanguage
        && a.Consent.Method == b.Consent.Method && a.Consent.At.ToUniversalTime() == b.Consent.At.ToUniversalTime()
        && a.Consent.StatementVersion == b.Consent.StatementVersion && a.Consent.Language == b.Consent.Language
        && (a.Consent.Witness ?? string.Empty) == (b.Consent.Witness ?? string.Empty)
        && a.Consent.Evidence == b.Consent.Evidence;

    private static PatientParameters Parameters(RoleSession session, Guid fhirId, PatientDto patient, string? phone) => new()
    {
        FhirId = fhirId,
        Full = $"{patient.GivenName.Trim()} {patient.FamilyName.Trim()}",
        Given = patient.GivenName.Trim(),
        Family = patient.FamilyName.Trim(),
        Sex = patient.Sex,
        Dob = patient.DateOfBirth,
        Estimated = patient.BirthDateEstimated,
        Phone = phone,
        Community = patient.Community.Trim(),
        District = patient.District.Trim(),
        Language = patient.PreferredLanguage,
        ConsentAt = patient.Consent.At.ToUniversalTime(),
        ConsentMethod = patient.Consent.Method,
        By = session.User.Id,
    };

    private sealed record PatientParameters
    {
        public Guid FhirId { get; init; }

        public string Full { get; init; } = string.Empty;

        public string Given { get; init; } = string.Empty;

        public string Family { get; init; } = string.Empty;

        public string Sex { get; init; } = string.Empty;

        public DateOnly Dob { get; init; }

        public bool Estimated { get; init; }

        public string? Phone { get; init; }

        public string Community { get; init; } = string.Empty;

        public string District { get; init; } = string.Empty;

        public string Language { get; init; } = string.Empty;

        public DateTimeOffset ConsentAt { get; init; }

        public string ConsentMethod { get; init; } = string.Empty;

        public string By { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public int InternalId { get; init; }

        public int ExpectedVersion { get; init; }
    }

    private sealed class PatientRow
    {
        public int InternalId { get; set; }

        public Guid FhirId { get; set; }

        public string? GivenName { get; set; }

        public string? FamilyName { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string? Gender { get; set; }

        public string DateOfBirth { get; set; } = "1900-01-01";

        public bool BirthDateEstimated { get; set; }

        public string? Phone { get; set; }

        public string? Community { get; set; }

        public string District { get; set; } = string.Empty;

        public string? PreferredLanguage { get; set; }

        public int Version { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime? ConsentGivenAt { get; set; }

        public string? ConsentMethod { get; set; }

        public string? StatementVersion { get; set; }

        public string? ConsentLanguage { get; set; }

        public string? Witness { get; set; }

        public Guid? EvidenceMediaId { get; set; }

        public string? EvidenceSha256 { get; set; }

        public string? EvidenceMime { get; set; }

        public string? NationalId { get; set; }

        public StoredPatient ToStored()
        {
            // Patients made before the app (the Lab 6 sample data) have only a full name: split it at the first space.
            var firstSpace = FullName.IndexOf(' ', StringComparison.Ordinal);
            var given = GivenName ?? (firstSpace < 0 ? FullName : FullName[..firstSpace]);
            var family = FamilyName ?? (firstSpace < 0 ? string.Empty : FullName[(firstSpace + 1)..]);
            var at = ConsentGivenAt ?? CreatedAt;

            var consent = new ConsentDto(
                Given: ConsentMethod is not null,
                Method: ConsentMethod ?? "spoken",
                At: new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)),
                StatementVersion: StatementVersion,
                Language: ConsentLanguage,
                ObtainedBy: null,
                Witness: Witness,
                Evidence: EvidenceMediaId is null || EvidenceSha256 is null
                    ? null
                    : new EvidenceDto(EvidenceMediaId.Value.ToString(), EvidenceSha256, EvidenceMime ?? string.Empty));

            return new StoredPatient(InternalId, new PatientDto(
                Id: FhirId.ToString(),
                GivenName: given,
                FamilyName: family,
                Sex: Gender is "female" or "male" or "other" ? Gender : "other",
                DateOfBirth: DateOnly.ParseExact(DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                BirthDateEstimated: BirthDateEstimated,
                Phone: Phone,
                NationalId: NationalId,
                Community: Community ?? string.Empty,
                District: District,
                PreferredLanguage: PreferredLanguage ?? "en",
                Consent: consent,
                CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
                UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)),
                Version: Version));
        }
    }
}
