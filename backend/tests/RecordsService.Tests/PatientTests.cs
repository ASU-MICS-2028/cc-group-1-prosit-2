using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;

namespace HealthConnect.RecordsService.Tests;

/// <summary>PUT /api/patients/{id}: saving a patient the phone made.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class PatientTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Chv = RecordsDatabase.Chv;
    private static readonly Worker Nurse = RecordsDatabase.Nurse;

    // ------------------------------------------------------------------ creating and repeating

    [Fact]
    public async Task A_new_patient_is_created_with_version_one_and_everything_the_app_sent()
    {
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, Patient(id), Chv);

        Assert.Equal(201, reply.Status);
        Assert.Equal(id.ToString(), reply.Body.GetProperty("id").GetString());
        Assert.Equal("Ama", reply.Body.GetProperty("givenName").GetString());
        Assert.Equal("Owusu", reply.Body.GetProperty("familyName").GetString());
        Assert.Equal("1980-07-01", reply.Body.GetProperty("dateOfBirth").GetString());
        Assert.True(reply.Body.GetProperty("birthDateEstimated").GetBoolean());
        Assert.Equal(1, reply.Body.GetProperty("version").GetInt32());
        Assert.Equal("spoken", reply.Body.GetProperty("consent").GetProperty("method").GetString());
        Assert.Equal("Synthetic Witness", reply.Body.GetProperty("consent").GetProperty("witness").GetString());
        Assert.Equal(Start.AddMinutes(-5), reply.Body.GetProperty("createdAt").GetDateTimeOffset());   // the phone's own time is kept
    }

    [Fact]
    public async Task The_database_holds_it_the_way_the_schema_wants_it()
    {
        var id = Guid.NewGuid();

        await PutPatientAsync(id, Patient(id), Chv);

        Assert.Equal("Ama Owusu", await ScalarAsync<string>("SELECT full_name FROM patients WHERE fhir_id = @id", new { id }));
        Assert.Equal("chv-042", await ScalarAsync<string>("SELECT created_by FROM patients WHERE fhir_id = @id", new { id }));
        Assert.Equal("spoken", await ScalarAsync<string>("SELECT consent_method FROM patients WHERE fhir_id = @id", new { id }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM consents WHERE status = 'active' AND witness = 'Synthetic Witness' AND statement_version = 'v1'"));
        Assert.Equal("chv-042", await ScalarAsync<string>("SELECT obtained_by FROM consents LIMIT 1"));   // the signed-in worker, not what the phone says
    }

    [Fact]
    public async Task Sending_the_same_patient_again_saves_it_once()
    {
        var id = Guid.NewGuid();

        var first = await PutPatientAsync(id, Patient(id), Chv);
        var again = await PutPatientAsync(id, Patient(id), Chv);

        Assert.Equal(201, first.Status);
        Assert.Equal(200, again.Status);
        Assert.Equal(1, again.Body.GetProperty("version").GetInt32());
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM patients"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM consents"));
    }

    [Fact]
    public async Task The_phone_number_is_stored_in_international_form_however_it_was_typed()
    {
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, Patient(id) with { Phone = "024 123 4567" }, Chv);

        Assert.Equal("+233241234567", reply.Body.GetProperty("phone").GetString());
    }

    // ------------------------------------------------------------------ versions and conflicts

    [Fact]
    public async Task Saving_at_the_version_the_server_has_changes_it_and_moves_the_version_on()
    {
        var id = await SavedPatientAsync();

        var reply = await PutPatientAsync(id, Patient(id, version: 1) with { Community = "Ayikuma" }, Chv);

        Assert.Equal(200, reply.Status);
        Assert.Equal(2, reply.Body.GetProperty("version").GetInt32());
        Assert.Equal("Ayikuma", reply.Body.GetProperty("community").GetString());
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM consents"));   // the consent did not change, so no new row
    }

    [Fact]
    public async Task A_changed_consent_is_added_to_the_history_and_never_overwrites_it()
    {
        var id = await SavedPatientAsync();
        var newConsent = Patient(id).Consent with { Method = "signature", At = Start, Witness = null, Evidence = new EvidenceDto(Guid.NewGuid().ToString(), new string('a', 64), "image/png") };

        var reply = await PutPatientAsync(id, Patient(id, version: 1) with { Consent = newConsent }, Chv);

        Assert.Equal(200, reply.Status);
        Assert.Equal(2, await ScalarAsync<int>("SELECT count(*) FROM consents"));
        Assert.Equal("signature", await ScalarAsync<string>("SELECT consent_method FROM patients"));
        Assert.Equal("signature", reply.Body.GetProperty("consent").GetProperty("method").GetString());
        Assert.Equal(new string('a', 64), reply.Body.GetProperty("consent").GetProperty("evidence").GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task A_stale_version_with_different_content_is_a_conflict_that_shows_what_the_server_holds()
    {
        var id = await SavedPatientAsync();
        await PutPatientAsync(id, Patient(id, version: 1) with { Community = "Ayikuma" }, Nurse);   // someone else changed it: now version 2

        var reply = await PutPatientAsync(id, Patient(id, version: 1) with { Community = "Doryumu" }, Chv);

        Assert.Equal(409, reply.Status);
        Assert.Equal("conflict", reply.Error);
        Assert.Equal("Ayikuma", reply.Body.GetProperty("current").GetProperty("community").GetString());
        Assert.Equal(2, reply.Body.GetProperty("current").GetProperty("version").GetInt32());
        Assert.Equal("Ayikuma", await ScalarAsync<string>("SELECT community FROM patients"));   // nothing was overwritten
    }

    [Fact]
    public async Task A_stale_version_that_says_the_same_thing_as_the_server_is_just_a_repeat()
    {
        var id = await SavedPatientAsync();
        await PutPatientAsync(id, Patient(id, version: 1) with { Community = "Ayikuma" }, Nurse);

        var reply = await PutPatientAsync(id, Patient(id, version: 1) with { Community = "Ayikuma" }, Chv);

        Assert.Equal(200, reply.Status);
        Assert.Equal(2, reply.Body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Two_saves_at_the_same_version_at_once_change_it_once_and_the_other_is_a_conflict()
    {
        var id = await SavedPatientAsync();

        var replies = await Task.WhenAll(
            PutPatientAsync(id, Patient(id, version: 1) with { Community = "Ayikuma" }, Chv),
            PutPatientAsync(id, Patient(id, version: 1) with { Community = "Doryumu" }, Chv));

        Assert.Equal([200, 409], replies.Select(r => r.Status).Order());
        Assert.Equal(2, await ScalarAsync<int>("SELECT version FROM patients"));
    }

    // ------------------------------------------------------------------ what must be refused

    public static TheoryData<string, Func<PatientDto, PatientDto>> BadPatients => new()
    {
        { "givenName", p => p with { GivenName = " " } },
        { "familyName", p => p with { FamilyName = string.Empty } },
        { "community", p => p with { Community = string.Empty } },
        { "sex", p => p with { Sex = "robot" } },
        { "preferredLanguage", p => p with { PreferredLanguage = "fr" } },
        { "dateOfBirth", p => p with { DateOfBirth = new DateOnly(2030, 1, 1) } },
        { "phone", p => p with { Phone = "12345" } },
        { "consent", p => p with { Consent = p.Consent with { Given = false } } },
        { "consent", p => p with { Consent = p.Consent with { Witness = null, Evidence = null } } },
        { "consent.method", p => p with { Consent = p.Consent with { Method = "verbal" } } },
        { "consent.statementVersion", p => p with { Consent = p.Consent with { StatementVersion = null } } },
        { "consent.language", p => p with { Consent = p.Consent with { Language = "fr" } } },
        { "consent.evidence.sha256", p => p with { Consent = p.Consent with { Evidence = new EvidenceDto(Guid.NewGuid().ToString(), "ABC", "image/png") } } },
        { "consent.evidence.mime", p => p with { Consent = p.Consent with { Evidence = new EvidenceDto(Guid.NewGuid().ToString(), new string('a', 64), "application/pdf") } } },
        { "consent.evidence.mediaId", p => p with { Consent = p.Consent with { Evidence = new EvidenceDto("nope", new string('a', 64), "image/png") } } },
        { "id", p => p with { Id = Guid.NewGuid().ToString() } },
        { "version", p => p with { Version = -1 } },
    };

    [Theory]
    [MemberData(nameof(BadPatients))]
    public async Task Something_wrong_in_a_patient_is_a_422_that_names_the_field_and_saves_nothing(string field, Func<PatientDto, PatientDto> spoil)
    {
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, spoil(Patient(id)), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError(field));
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM patients"));
    }

    [Fact]
    public async Task A_recording_instead_of_a_witness_is_enough_proof_of_consent()
    {
        var id = Guid.NewGuid();
        var consent = Patient(id).Consent with { Witness = null, Evidence = new EvidenceDto(Guid.NewGuid().ToString(), new string('b', 64), "audio/webm") };

        var reply = await PutPatientAsync(id, Patient(id) with { Consent = consent }, Chv);

        Assert.Equal(201, reply.Status);
        Assert.Equal("audio/webm", reply.Body.GetProperty("consent").GetProperty("evidence").GetProperty("mime").GetString());
        Assert.StartsWith($"media/{id}/", await ScalarAsync<string>("SELECT evidence_key FROM consents"));
    }

    // ------------------------------------------------------------------ the national id

    [Fact]
    public async Task A_national_id_is_filed_as_an_identifier_and_comes_back()
    {
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, Patient(id) with { NationalId = " GHA-000000001-1 " }, Chv);

        Assert.Equal("GHA-000000001-1", reply.Body.GetProperty("nationalId").GetString());
        Assert.Equal("GHA-000000001-1", await ScalarAsync<string>("SELECT value FROM patient_identifiers"));
    }

    [Fact]
    public async Task One_national_id_belongs_to_one_patient()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await PutPatientAsync(first, Patient(first) with { NationalId = "GHA-000000001-1" }, Chv);

        var reply = await PutPatientAsync(second, Patient(second) with { NationalId = "GHA-000000001-1" }, Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError("nationalId"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM patients"));   // the second patient was not saved at all
    }

    [Fact]
    public async Task A_new_national_id_replaces_the_old_one_which_is_kept_as_secondary_and_none_is_ever_cleared()
    {
        var id = Guid.NewGuid();
        await PutPatientAsync(id, Patient(id) with { NationalId = "GHA-000000001-1" }, Chv);

        var changed = await PutPatientAsync(id, Patient(id, version: 1) with { NationalId = "GHA-000000002-2" }, Chv);
        var cleared = await PutPatientAsync(id, Patient(id, version: 2) with { NationalId = null, Community = "Ayikuma" }, Chv);

        Assert.Equal("GHA-000000002-2", changed.Body.GetProperty("nationalId").GetString());
        Assert.Equal("GHA-000000002-2", cleared.Body.GetProperty("nationalId").GetString());   // sending none does not remove it
        Assert.Equal(["secondary"], await QueryAsync<string>("SELECT use FROM patient_identifiers WHERE value = 'GHA-000000001-1'"));
    }

    // ------------------------------------------------------------------ districts and workers

    [Fact]
    public async Task A_CHV_cannot_save_a_patient_in_another_district()
    {
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, Patient(id, district: "Kintampo North"), Chv);

        Assert.Equal(403, reply.Status);
        Assert.Equal("wrong_district", reply.Error);
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM patients"));
    }

    [Fact]
    public async Task A_nurse_can_save_a_patient_in_any_district()
    {
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, Patient(id, district: "Kintampo North"), Nurse);

        Assert.Equal(201, reply.Status);
        Assert.Equal("Kintampo North", reply.Body.GetProperty("district").GetString());
    }

    [Fact]
    public async Task A_CHV_cannot_take_over_a_patient_that_exists_in_a_district_they_cannot_see()
    {
        var id = await SavedPatientAsync(Nurse, district: "Kintampo North");

        var reply = await PutPatientAsync(id, Patient(id, district: "Shai-Osudoku", version: 1) with { GivenName = "Hijacked" }, Chv);

        Assert.Equal(403, reply.Status);
        Assert.Equal("Ama", await ScalarAsync<string>("SELECT given_name FROM patients"));
    }

    [Fact]
    public async Task A_CHV_updates_a_patient_in_their_own_district_but_cannot_move_it_to_another()
    {
        var id = await SavedPatientAsync();

        var moved = await PutPatientAsync(id, Patient(id, district: "Kintampo North", version: 1), Chv);

        Assert.Equal(403, moved.Status);
        Assert.Equal("Shai-Osudoku", await ScalarAsync<string>("SELECT district FROM patients"));
    }

    [Fact]
    public async Task A_worker_who_has_been_switched_off_is_refused_at_once_even_with_a_valid_token()
    {
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'chv-042' RETURNING 1");
        var id = Guid.NewGuid();

        var reply = await PutPatientAsync(id, Patient(id), Chv);

        Assert.Equal(401, reply.Status);
        Assert.Equal("unknown_user", reply.Error);
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM patients"));
    }

    [Fact]
    public async Task A_token_with_no_worker_in_it_is_refused()
    {
        var id = Guid.NewGuid();

        var result = await HealthConnect.RecordsService.Features.PutPatient.Handle(
            id, Patient(id), new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), Sessions, Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HealthConnect.RecordsService.Features.PutPatient>.Instance, CancellationToken.None);

        Assert.Equal(401, (await ResultReader.RunAsync(result)).Status);
    }

    // ------------------------------------------------------------------ pulling

    [Fact]
    public async Task A_CHV_pulls_only_their_own_districts_patients_and_a_nurse_pulls_everyones()
    {
        await SavedPatientAsync();
        await SavedPatientAsync(Nurse, district: "Kintampo North");

        var chv = await PullPatientsAsync(Chv);
        var nurse = await PullPatientsAsync(Nurse);

        Assert.Equal(1, chv.Body.GetArrayLength());
        Assert.Equal("Shai-Osudoku", chv.Body[0].GetProperty("district").GetString());
        Assert.Equal(2, nurse.Body.GetArrayLength());
    }

    [Fact]
    public async Task Pulling_with_updatedSince_returns_only_what_changed_after_it_oldest_change_first()
    {
        var first = await SavedPatientAsync();
        await Task.Delay(30);
        var middle = DateTimeOffset.UtcNow;
        await Task.Delay(30);
        var second = await SavedPatientAsync();
        await PutPatientAsync(first, Patient(first, version: 1) with { Community = "Ayikuma" }, Chv);   // the first one changes last

        var all = await PullPatientsAsync(Nurse);
        var after = await PullPatientsAsync(Nurse, middle);

        Assert.Equal(2, all.Body.GetArrayLength());
        Assert.Equal(second.ToString(), after.Body[0].GetProperty("id").GetString());   // the oldest change first
        Assert.Equal(first.ToString(), after.Body[1].GetProperty("id").GetString());
        Assert.Equal("Ayikuma", after.Body[1].GetProperty("community").GetString());
    }

    [Fact]
    public async Task What_is_pulled_is_the_same_shape_the_phone_saves_including_consent_and_national_id()
    {
        var id = Guid.NewGuid();
        await PutPatientAsync(id, Patient(id) with { NationalId = "GHA-000000001-1", Phone = "0241234567" }, Chv);

        var pulled = (await PullPatientsAsync(Nurse)).Body[0];

        Assert.Equal(
            ["id", "givenName", "familyName", "sex", "dateOfBirth", "birthDateEstimated", "phone", "nationalId", "community", "district", "preferredLanguage", "consent", "createdAt", "updatedAt", "version"],
            pulled.EnumerateObject().Select(p => p.Name));
        Assert.Equal("+233241234567", pulled.GetProperty("phone").GetString());
        Assert.Equal("Synthetic Witness", pulled.GetProperty("consent").GetProperty("witness").GetString());
    }
}
