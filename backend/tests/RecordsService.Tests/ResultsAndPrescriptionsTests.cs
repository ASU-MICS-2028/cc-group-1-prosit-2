using System.Web;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using HealthConnect.RecordsService.Events;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Media;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HealthConnect.RecordsService.Tests;

/// <summary>Test results (lab slips, X-ray films), prescriptions, and fetching another worker's photos.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class ResultsAndPrescriptionsTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Chv = RecordsDatabase.Chv;
    private static readonly Worker OtherChv = RecordsDatabase.OtherChv;
    private static readonly Worker Nurse = RecordsDatabase.Nurse;
    private static readonly Worker Doctor = RecordsDatabase.Doctor;

    // ------------------------------------------------------------------ test results

    private static TestResultDto Result(Guid id, Guid patient, string status = "requested", string category = "imaging", string test = "Chest X-ray", int version = 0, string? result = null) => new(
        Id: id.ToString(),
        PatientId: patient.ToString(),
        Category: category,
        Test: test,
        Status: status,
        Facility: "Dodowa District Hospital",
        Result: result,
        Flag: status == "resulted" ? "abnormal" : null,
        Notes: null,
        RequestedAt: Start.AddMinutes(-10),
        ResultedAt: status == "resulted" ? Start.AddMinutes(-1) : null,
        CreatedBy: null,
        CreatedAt: null,
        UpdatedAt: null,
        Version: version);

    private async Task<Reply> PutResultAsync(Guid id, TestResultDto r, Worker who) =>
        await ResultReader.RunAsync(await PutTestResult.Handle(id, r, As(who), Sessions, Time, NullLogger<PutTestResult>.Instance, Events, new AppointmentEventOptions("arn:test", "test"), CancellationToken.None));

    [Fact]
    public async Task An_x_ray_is_sent_out_then_its_result_is_added_when_the_patient_comes_back()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var sent = await PutResultAsync(id, Result(id, patient), Chv);
        Assert.Equal(201, sent.Status);
        Assert.Equal("requested", sent.Body.GetProperty("status").GetString());
        Assert.Equal("chv-042", sent.Body.GetProperty("createdBy").GetString());

        var back = await PutResultAsync(id, Result(id, patient, "resulted", version: 1, result: "Right lower lobe opacity"), Chv);
        Assert.Equal(200, back.Status);
        Assert.Equal("resulted", back.Body.GetProperty("status").GetString());
        Assert.Equal("Right lower lobe opacity", back.Body.GetProperty("result").GetString());
        Assert.Equal(2, back.Body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_quick_test_is_saved_with_its_answer_and_a_repeat_is_harmless()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        var rdt = Result(id, patient, "resulted", "rapid", "Malaria RDT", result: "Positive");

        Assert.Equal(201, (await PutResultAsync(id, rdt, Chv)).Status);
        Assert.Equal(200, (await PutResultAsync(id, rdt, Chv)).Status);   // the phone lost the reply and sent it again
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM test_results"));
    }

    [Fact]
    public async Task A_result_cannot_go_back_to_requested_or_change_its_test()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutResultAsync(id, Result(id, patient, "resulted", result: "Clear"), Chv);

        Assert.Equal(409, (await PutResultAsync(id, Result(id, patient, "requested", version: 1), Chv)).Status);
        Assert.Equal(422, (await PutResultAsync(id, Result(id, patient, "resulted", test: "Limb X-ray", version: 1, result: "Clear"), Chv)).Status);
    }

    [Fact]
    public async Task A_result_without_its_time_or_with_a_made_up_category_is_refused()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        Assert.Equal(422, (await PutResultAsync(id, Result(id, patient, "resulted") with { ResultedAt = null }, Chv)).Status);
        Assert.Equal(422, (await PutResultAsync(id, Result(id, patient, category: "mri"), Chv)).Status);
    }

    [Fact]
    public async Task A_chv_in_another_district_neither_saves_nor_sees_the_test()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        Assert.Equal(422, (await PutResultAsync(id, Result(id, patient), OtherChv)).Status);   // the patient is invisible to them
        await PutResultAsync(id, Result(id, patient), Chv);

        var theirs = await ResultReader.RunAsync(await GetTestResults.Handle(As(OtherChv), Sessions, CancellationToken.None));
        var nurse = await ResultReader.RunAsync(await GetTestResults.Handle(As(Nurse), Sessions, CancellationToken.None));
        Assert.Equal(0, theirs.Body.GetArrayLength());
        Assert.Equal(1, nurse.Body.GetArrayLength());
    }

    // ------------------------------------------------------------------ prescriptions

    private static PrescriptionDto Rx(Guid id, Guid patient, string status = "active", int version = 0, string? pharmacy = null, string medicine = "Artemether-lumefantrine 20/120 mg") => new(
        Id: id.ToString(),
        PatientId: patient.ToString(),
        AppointmentId: null,
        Code: PutPrescription.CodeFor(id),
        Items: [new PrescriptionItemDto(medicine, "4 tablets", "Twice a day", 3, "After food")],
        Notes: null,
        Status: status,
        PrescribedBy: null,
        PrescriberName: null,
        PrescribedAt: Start.AddMinutes(-1),
        DispensedBy: null,
        DispensedAt: null,
        Pharmacy: pharmacy,
        CreatedAt: null,
        UpdatedAt: null,
        Version: version);

    private async Task<Reply> PutRxAsync(Guid id, PrescriptionDto r, Worker who) =>
        await ResultReader.RunAsync(await PutPrescription.Handle(id, r, As(who), Sessions, Time, NullLogger<PutPrescription>.Instance, Events, new AppointmentEventOptions("arn:test", "test"), CancellationToken.None));

    [Fact]
    public void The_code_is_made_from_the_id()
    {
        Assert.Equal("RX-1A2B-3C4D", PutPrescription.CodeFor(Guid.Parse("1a2b3c4d-0000-4000-8000-000000000000")));
    }

    [Fact]
    public async Task A_doctor_prescribes_and_the_name_comes_from_the_sign_in()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutRxAsync(id, Rx(id, patient) with { PrescriberName = "Someone Else" }, Doctor);

        Assert.Equal(201, reply.Status);
        Assert.Equal("dr-eshun", reply.Body.GetProperty("prescribedBy").GetString());
        Assert.Equal("Kwesi Eshun", reply.Body.GetProperty("prescriberName").GetString());
        Assert.Equal(PutPrescription.CodeFor(id), reply.Body.GetProperty("code").GetString());
        Assert.Equal(3, reply.Body.GetProperty("items")[0].GetProperty("days").GetInt32());
    }

    [Theory]
    [InlineData("chv")]
    [InlineData("nurse")]
    public async Task Only_a_doctor_writes_a_prescription(string role)
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutRxAsync(id, Rx(id, patient), role == "chv" ? Chv : Nurse);

        Assert.Equal(403, reply.Status);
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM prescriptions"));
    }

    [Fact]
    public async Task A_code_that_does_not_match_the_id_is_refused()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        Assert.Equal(422, (await PutRxAsync(id, Rx(id, patient) with { Code = "RX-0000-0000" }, Doctor)).Status);
    }

    [Fact]
    public async Task The_chv_records_which_pharmacy_gave_it_out_and_it_is_given_out_once()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutRxAsync(id, Rx(id, patient), Doctor);

        var given = await PutRxAsync(id, Rx(id, patient, "dispensed", 1, "Dodowa Community Pharmacy"), Chv);

        Assert.Equal(200, given.Status);
        Assert.Equal("dispensed", given.Body.GetProperty("status").GetString());
        Assert.Equal("chv-042", given.Body.GetProperty("dispensedBy").GetString());
        Assert.Equal("Dodowa Community Pharmacy", given.Body.GetProperty("pharmacy").GetString());
        Assert.Equal(409, (await PutRxAsync(id, Rx(id, patient, "cancelled", 2), Doctor)).Status);
    }

    [Fact]
    public async Task Medicines_never_change_and_only_a_doctor_cancels()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutRxAsync(id, Rx(id, patient), Doctor);

        Assert.Equal(422, (await PutRxAsync(id, Rx(id, patient, version: 1, medicine: "Amoxicillin 500 mg"), Doctor)).Status);
        Assert.Equal(403, (await PutRxAsync(id, Rx(id, patient, "cancelled", 1), Chv)).Status);
        Assert.Equal(200, (await PutRxAsync(id, Rx(id, patient, "cancelled", 1), Doctor)).Status);
    }

    [Fact]
    public async Task Dispensing_needs_the_pharmacy()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutRxAsync(id, Rx(id, patient), Doctor);

        Assert.Equal(422, (await PutRxAsync(id, Rx(id, patient, "dispensed", 1), Chv)).Status);
    }

    [Fact]
    public async Task The_chv_pulls_the_doctors_prescription_and_another_district_does_not()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutRxAsync(id, Rx(id, patient), Doctor);

        var mine = await ResultReader.RunAsync(await GetPrescriptions.Handle(As(Chv), Sessions, CancellationToken.None));
        var theirs = await ResultReader.RunAsync(await GetPrescriptions.Handle(As(OtherChv), Sessions, CancellationToken.None));
        Assert.Equal(1, mine.Body.GetArrayLength());
        Assert.Equal(0, theirs.Body.GetArrayLength());
    }

    // ------------------------------------------------------------------ photos other workers took

    private static readonly S3MediaUrlSigner Signer = new(new AmazonS3Client(new BasicAWSCredentials("AKIATESTTESTTESTTEST", "secret-secret-secret-secret-secret-0000"), RegionEndpoint.EUWest1));

    private async Task<Guid> SlipPhotoAsync(Guid patient, Guid result)
    {
        var id = Guid.NewGuid();
        var reply = await ResultReader.RunAsync(await PostMediaUploadUrl.Handle(
            id,
            new MediaUploadRequest("result-photo", "image/jpeg", 300_000, new string('b', 64), patient.ToString(), "result", result.ToString()),
            As(Chv), Sessions, Signer, new MediaOptions("test-media"), new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<PostMediaUploadUrl>.Instance, CancellationToken.None));
        Assert.Equal(201, reply.Status);
        return id;
    }

    [Fact]
    public async Task The_doctor_lists_the_chvs_photo_of_the_slip_and_gets_a_link_to_read_it()
    {
        var patient = await SavedPatientAsync();
        var result = Guid.NewGuid();
        await PutResultAsync(result, Result(result, patient, "resulted", result: "See photo"), Chv);
        var photo = await SlipPhotoAsync(patient, result);

        var list = await ResultReader.RunAsync(await GetMediaList.Handle(As(Doctor), Sessions, CancellationToken.None));
        Assert.Equal(1, list.Body.GetArrayLength());
        Assert.Equal(result.ToString(), list.Body[0].GetProperty("ownerId").GetString());
        Assert.Equal("result-photo", list.Body[0].GetProperty("kind").GetString());

        var link = await ResultReader.RunAsync(await GetMediaDownloadUrl.Handle(
            photo, As(Doctor), Sessions, Signer, new MediaOptions("test-media"), new FakeTimeProvider(DateTimeOffset.UtcNow), CancellationToken.None));
        Assert.Equal(200, link.Status);
        var url = new Uri(link.Body.GetProperty("url").GetString()!);
        Assert.Equal($"/media/{patient}/{photo}", url.AbsolutePath);
        Assert.Equal("300", HttpUtility.ParseQueryString(url.Query)["X-Amz-Expires"]);
    }

    [Fact]
    public async Task A_chv_in_another_district_gets_no_link_and_no_hint_the_photo_exists()
    {
        var patient = await SavedPatientAsync();
        var result = Guid.NewGuid();
        await PutResultAsync(result, Result(result, patient, "resulted", result: "See photo"), Chv);
        var photo = await SlipPhotoAsync(patient, result);

        var link = await ResultReader.RunAsync(await GetMediaDownloadUrl.Handle(
            photo, As(OtherChv), Sessions, Signer, new MediaOptions("test-media"), new FakeTimeProvider(DateTimeOffset.UtcNow), CancellationToken.None));
        var list = await ResultReader.RunAsync(await GetMediaList.Handle(As(OtherChv), Sessions, CancellationToken.None));

        Assert.Equal(404, link.Status);
        Assert.Equal(0, list.Body.GetArrayLength());
    }

    // ------------------------------------------------------------------ events for texts and alerts

    [Fact]
    public async Task A_new_prescription_is_published_with_its_code_and_the_patients_phone_but_not_their_name()
    {
        var patient = Guid.NewGuid();
        Assert.Equal(201, (await PutPatientAsync(patient, Patient(patient) with { Phone = "+233200009999" }, Chv)).Status);
        var id = Guid.NewGuid();

        await PutRxAsync(id, Rx(id, patient), Doctor);
        await PutRxAsync(id, Rx(id, patient, "dispensed", 1, "Dodowa Community Pharmacy"), Chv);

        var published = Assert.Single(Events.PublishedCare);
        Assert.Equal(CareEventTypes.PrescriptionCreated, published.Type);
        Assert.Equal(PutPrescription.CodeFor(id), published.Prescription!.Code);
        Assert.Equal("+233200009999", published.Patient.Phone);
        Assert.Equal("tw", published.Patient.PreferredLanguage);
    }

    [Fact]
    public async Task An_abnormal_result_alerts_once_and_a_normal_or_waiting_one_does_not()
    {
        var patient = await SavedPatientAsync();
        var xray = Guid.NewGuid();
        var rdt = Guid.NewGuid();

        await PutResultAsync(xray, Result(xray, patient), Chv);                                                   // sent out: nothing yet
        await PutResultAsync(xray, Result(xray, patient, "resulted", version: 1, result: "Opacity"), Chv);         // back, not normal
        await PutResultAsync(xray, Result(xray, patient, "resulted", version: 2, result: "Right lower lobe opacity"), Chv);   // reworded
        await PutResultAsync(rdt, Result(rdt, patient, "resulted", "rapid", "Malaria RDT") with { Flag = "normal" }, Chv);

        var alert = Assert.Single(Events.PublishedCare);
        Assert.Equal(CareEventTypes.ResultAbnormal, alert.Type);
        Assert.Equal("Chest X-ray", alert.Result!.Test);
        Assert.Null(alert.Patient.Phone);   // the clinicians open the app; the alert names no one
    }
}
