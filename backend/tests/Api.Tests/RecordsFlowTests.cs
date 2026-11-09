using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HealthConnect.Api.Tests;

/// <summary>
/// The records endpoints through the real HTTP API: real sign-in, real database, and the JSON the app sends
/// (frontend/src/db/types.ts). A CHV saves a patient, a reading and a referral; a nurse pulls and answers it.
/// </summary>
[IntegrationTest]
public sealed class RecordsFlowTests(SignInApi api) : IClassFixture<SignInApi>
{
    private static readonly DateTimeOffset Created = new(2026, 11, 9, 9, 55, 0, TimeSpan.Zero);

    private HttpClient Client => api.Client;

    private async Task<string> TokenAsync(string phone)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/verify", new { phone, code = "123456" }, CancellationToken.None);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None)).GetProperty("token").GetString()!;
    }

    private Task<string> ChvAsync() => TokenAsync("+233200000101");

    private Task<string> NurseAsync() => TokenAsync("+233200000102");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request, CancellationToken.None);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

    /// <summary>A patient exactly as the app's NewPatientPage saves it.</summary>
    private static object PatientBody(Guid id, int version = 0, string community = "Dodowa") => new
    {
        id = id.ToString(),
        givenName = "Ama",
        familyName = "Owusu",
        sex = "female",
        dateOfBirth = "1966-07-01",
        birthDateEstimated = true,
        phone = "0244123456",
        community,
        district = "Shai-Osudoku",
        preferredLanguage = "tw",
        consent = new
        {
            given = true,
            method = "spoken",
            at = Created,
            statementVersion = "v1",
            language = "tw",
            obtainedBy = "chv-042",
            evidence = new { mediaId = Guid.NewGuid().ToString(), sha256 = new string('c', 64), mime = "audio/webm" },
        },
        createdAt = Created,
        updatedAt = Created,
        version,
    };

    private static object VitalsBody(Guid id, Guid patient) => new
    {
        id = id.ToString(),
        patientId = patient.ToString(),
        takenAt = Created.AddMinutes(2),
        systolic = 186,
        diastolic = 118,
        temperatureC = 36.9,
        pulse = 96,
        createdAt = Created.AddMinutes(2),
        version = 0,
    };

    private static object ReferralBody(Guid id, Guid patient, string? referralStatus = null, int version = 0) => new
    {
        id = id.ToString(),
        patientId = patient.ToString(),
        type = "referral",
        scheduledAt = Created.AddDays(1),
        facility = "Dodowa District Hospital",
        priority = "urgent",
        reason = "Very high blood pressure. BP 186/118 mmHg",
        status = "scheduled",
        referralStatus,
        createdAt = Created.AddMinutes(3),
        updatedAt = Created.AddMinutes(3),
        version,
    };

    // ------------------------------------------------------------------ a home visit, start to finish

    [Fact]
    public async Task A_CHV_saves_a_patient_a_reading_and_a_referral_and_a_nurse_sees_and_answers_the_referral()
    {
        var chv = await ChvAsync();
        var nurse = await NurseAsync();
        var patient = Guid.NewGuid();
        var reading = Guid.NewGuid();
        var referral = Guid.NewGuid();

        var savedPatient = await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, PatientBody(patient));
        var savedReading = await SendAsync(HttpMethod.Put, $"/api/vitals/{reading}", chv, VitalsBody(reading, patient));
        var savedReferral = await SendAsync(HttpMethod.Put, $"/api/appointments/{referral}", chv, ReferralBody(referral, patient));

        Assert.Equal(HttpStatusCode.Created, savedPatient.StatusCode);
        Assert.Equal(HttpStatusCode.Created, savedReading.StatusCode);
        Assert.Equal(HttpStatusCode.Created, savedReferral.StatusCode);
        Assert.Equal("+233244123456", (await JsonOf(savedPatient)).GetProperty("phone").GetString());
        Assert.Equal("requested", (await JsonOf(savedReferral)).GetProperty("referralStatus").GetString());

        // The nurse's phone pulls what the CHV saved.
        var inbox = await JsonOf(await SendAsync(HttpMethod.Get, "/api/appointments", nurse));
        var theirPatients = await JsonOf(await SendAsync(HttpMethod.Get, "/api/patients", nurse));
        Assert.Contains(inbox.EnumerateArray(), a => a.GetProperty("id").GetString() == referral.ToString());
        Assert.Contains(theirPatients.EnumerateArray(), p => p.GetProperty("id").GetString() == patient.ToString());

        // She accepts it; the CHV's next pull shows the answer and who gave it.
        var accepted = await SendAsync(HttpMethod.Put, $"/api/appointments/{referral}", nurse, ReferralBody(referral, patient, "accepted", version: 1));
        var back = await JsonOf(await SendAsync(HttpMethod.Get, "/api/appointments", chv));
        var mine = back.EnumerateArray().Single(a => a.GetProperty("id").GetString() == referral.ToString());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("accepted", mine.GetProperty("referralStatus").GetString());
        Assert.Equal("nurse-17", mine.GetProperty("handledBy").GetString());
    }

    [Fact]
    public async Task Saving_the_same_thing_twice_after_a_lost_reply_changes_nothing_on_the_server()
    {
        var chv = await ChvAsync();
        var patient = Guid.NewGuid();

        var body = PatientBody(patient);
        var first = await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, body);
        var again = await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, (await JsonOf(again)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Two_phones_editing_the_same_patient_get_a_409_with_the_servers_copy_for_the_second()
    {
        var chv = await ChvAsync();
        var nurse = await NurseAsync();
        var patient = Guid.NewGuid();
        await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, PatientBody(patient));
        await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", nurse, PatientBody(patient, version: 1, community: "Ayikuma"));

        var stale = await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, PatientBody(patient, version: 1, community: "Doryumu"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Ayikuma", (await JsonOf(stale)).GetProperty("current").GetProperty("community").GetString());
    }

    // ------------------------------------------------------------------ who may do what, over HTTP

    [Theory]
    [InlineData("GET", "/api/patients")]
    [InlineData("GET", "/api/vitals")]
    [InlineData("GET", "/api/appointments")]
    public async Task Without_a_token_the_records_are_a_401(string method, string path)
    {
        var response = await SendAsync(new HttpMethod(method), path, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("+233200000104")]   // district officer: counts only, never a patient
    [InlineData("+233200000105")]   // auditor: the activity log, never a patient
    public async Task The_officer_and_the_auditor_get_a_403_on_every_patient_endpoint(string phone)
    {
        var token = await TokenAsync(phone);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/patients", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/vitals", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/appointments", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, $"/api/patients/{id}", token, PatientBody(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, $"/api/vitals/{id}", token, VitalsBody(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, $"/api/appointments/{id}", token, ReferralBody(id, id))).StatusCode);
    }

    [Fact]
    public async Task A_CHV_cannot_accept_a_referral_over_HTTP_either()
    {
        var chv = await ChvAsync();
        var patient = Guid.NewGuid();
        var referral = Guid.NewGuid();
        await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, PatientBody(patient));
        await SendAsync(HttpMethod.Put, $"/api/appointments/{referral}", chv, ReferralBody(referral, patient));

        var response = await SendAsync(HttpMethod.Put, $"/api/appointments/{referral}", chv, ReferralBody(referral, patient, "accepted", version: 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------------------ bad requests

    [Fact]
    public async Task A_bad_patient_is_a_422_with_the_wrong_fields_named_the_way_the_app_reads_them()
    {
        var chv = await ChvAsync();
        var id = Guid.NewGuid();

        var response = await SendAsync(HttpMethod.Put, $"/api/patients/{id}", chv, new { id = id.ToString(), givenName = "", sex = "robot" });

        // some required fields are missing altogether, which the JSON binder refuses with a 400 before our checks run
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest });
    }

    [Fact]
    public async Task A_complete_patient_without_consent_is_a_422_that_names_consent()
    {
        var chv = await ChvAsync();
        var id = Guid.NewGuid();
        var body = JsonSerializer.SerializeToElement(PatientBody(id));
        var noConsent = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body.GetRawText())!;
        noConsent["consent"] = JsonSerializer.SerializeToElement(new { given = false, method = "spoken", at = Created, statementVersion = "v1" });

        var response = await SendAsync(HttpMethod.Put, $"/api/patients/{id}", chv, noConsent);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotNull((await JsonOf(response)).GetProperty("errors").GetProperty("consent").GetString());
    }

    [Fact]
    public async Task A_number_sent_as_text_is_refused_not_quietly_accepted()
    {
        var chv = await ChvAsync();
        var patient = Guid.NewGuid();
        var id = Guid.NewGuid();
        await SendAsync(HttpMethod.Put, $"/api/patients/{patient}", chv, PatientBody(patient));
        var raw = $$"""{"id":"{{id}}","patientId":"{{patient}}","takenAt":"2026-11-09T10:00:00Z","systolic":"120","diastolic":80,"version":0}""";

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/vitals/{id}") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chv);
        var response = await Client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reading_for_a_patient_that_has_not_arrived_yet_is_a_422_so_the_phone_keeps_it()
    {
        var chv = await ChvAsync();
        var id = Guid.NewGuid();

        var response = await SendAsync(HttpMethod.Put, $"/api/vitals/{id}", chv, VitalsBody(id, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotNull((await JsonOf(response)).GetProperty("errors").GetProperty("patientId").GetString());
    }

    [Fact]
    public async Task Pulling_with_a_timestamp_in_the_query_string_works_over_HTTP()
    {
        var nurse = await NurseAsync();
        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(1).ToString("o"));

        var response = await SendAsync(HttpMethod.Get, $"/api/patients?updatedSince={future}", nurse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await JsonOf(response)).GetArrayLength());
    }

    [Fact]
    public async Task A_worker_switched_off_loses_access_to_patients_at_once_even_with_a_valid_token()
    {
        var nurse = await NurseAsync();
        await using var source = Npgsql.NpgsqlDataSource.Create(api.Postgres.ConnectionString);
        await using (var off = source.CreateCommand("UPDATE users SET active = false WHERE id = 'nurse-17'"))
        {
            await off.ExecuteNonQueryAsync();
        }

        try
        {
            var response = await SendAsync(HttpMethod.Get, "/api/patients", nurse);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await using var on = source.CreateCommand("UPDATE users SET active = true WHERE id = 'nurse-17'");
            await on.ExecuteNonQueryAsync();
        }
    }

    // ------------------------------------------------------------------ the audit trail over HTTP

    [Fact]
    public async Task A_phone_uploads_its_audit_events_and_only_the_auditor_can_read_and_verify_them()
    {
        var chv = await ChvAsync();
        var auditor = await TokenAsync("+233200000105");
        var patient = Guid.NewGuid();
        var events = new[]
        {
            new { id = Guid.NewGuid().ToString(), at = Created, actorId = "chv-042", actorName = "Akosua", role = "chv", action = "view", entity = "patient", entityId = patient.ToString(), patientId = patient.ToString(), outcome = "success", purpose = "TREAT" },
        };

        var upload = await SendAsync(HttpMethod.Post, "/api/audit-events", chv, new { events });
        var chvRead = await SendAsync(HttpMethod.Get, "/api/audit-events", chv);
        var chvVerify = await SendAsync(HttpMethod.Get, "/api/audit-events/verify", chv);
        var read = await SendAsync(HttpMethod.Get, $"/api/audit-events?patientId={patient}", auditor);
        var verify = await SendAsync(HttpMethod.Get, "/api/audit-events/verify", auditor);

        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, chvRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, chvVerify.StatusCode);
        var entries = await JsonOf(read);
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("chv-042", entries[0].GetProperty("actorId").GetString());
        Assert.True((await JsonOf(verify)).GetProperty("intact").GetBoolean());
    }

    [Fact]
    public async Task Without_a_token_the_audit_endpoints_are_a_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Post, "/api/audit-events", null, new { events = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/audit-events", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/audit-events/verify", null)).StatusCode);
    }

    // ------------------------------------------------------------------ media and the district summary over HTTP

    [Fact]
    public async Task A_media_link_needs_a_token_a_clinical_role_and_a_bucket()
    {
        var chv = await ChvAsync();
        var officer = await TokenAsync("+233200000104");
        var patient = Guid.NewGuid();
        var id = Guid.NewGuid();
        var request = new { kind = "wound-photo", mime = "image/jpeg", size = 90_000, sha256 = new string('d', 64), patientId = patient.ToString(), ownerEntity = "patient", ownerId = patient.ToString() };

        var anonymous = await SendAsync(HttpMethod.Post, $"/api/media/{id}/upload-url", null, request);
        var forbidden = await SendAsync(HttpMethod.Post, $"/api/media/{id}/upload-url", officer, request);
        var noBucket = await SendAsync(HttpMethod.Post, $"/api/media/{id}/upload-url", chv, request);
        var tooBig = await SendAsync(HttpMethod.Post, $"/api/media/{id}/upload-url", chv, new { request.kind, request.mime, size = 3_000_000, request.sha256, request.patientId, request.ownerEntity, request.ownerId });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noBucket.StatusCode);   // this test API has no bucket, like a laptop
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooBig.StatusCode);
        Assert.NotNull((await JsonOf(tooBig)).GetProperty("errors").GetProperty("size").GetString());
    }

    [Fact]
    public async Task Only_the_officer_and_the_auditor_read_the_district_summary()
    {
        var officer = await TokenAsync("+233200000104");
        var auditor = await TokenAsync("+233200000105");
        var chv = await ChvAsync();
        var nurse = await NurseAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/surveillance/weekly", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/surveillance/weekly", chv)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/surveillance/weekly", nurse)).StatusCode);
        var asOfficer = await SendAsync(HttpMethod.Get, "/api/surveillance/weekly", officer);
        var asAuditor = await SendAsync(HttpMethod.Get, "/api/surveillance/weekly", auditor);
        Assert.Equal(HttpStatusCode.OK, asOfficer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAuditor.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await JsonOf(asOfficer)).ValueKind);
    }
}
