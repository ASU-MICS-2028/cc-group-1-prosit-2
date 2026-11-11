using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;
using HealthConnect.RecordsService.Validation;

namespace HealthConnect.RecordsService.Tests;

/// <summary>PUT /api/appointments/{id}: visits, referrals and teleconsults, and the referral journey between a CHV and a nurse or doctor.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class AppointmentTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Chv = RecordsDatabase.Chv;
    private static readonly Worker Nurse = RecordsDatabase.Nurse;
    private static readonly Worker Doctor = RecordsDatabase.Doctor;

    private async Task<(Guid Patient, Guid Referral)> ReferralRequestedAsync()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        var reply = await PutAppointmentAsync(id, Appointment(id, patient), Chv);
        Assert.Equal(201, reply.Status);
        return (patient, id);
    }

    // ------------------------------------------------------------------ creating

    [Fact]
    public async Task A_new_referral_starts_as_requested_even_if_the_phone_does_not_say()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, patient), Chv);

        Assert.Equal(201, reply.Status);
        Assert.Equal("requested", reply.Body.GetProperty("referralStatus").GetString());
        Assert.Equal("urgent", reply.Body.GetProperty("priority").GetString());
        Assert.Equal("Dodowa District Hospital", reply.Body.GetProperty("facility").GetString());
        Assert.Equal(1, reply.Body.GetProperty("version").GetInt32());
        Assert.Equal(patient.ToString(), reply.Body.GetProperty("patientId").GetString());
    }

    [Theory]
    [InlineData("follow_up")]
    [InlineData("antenatal")]
    [InlineData("postnatal")]
    public async Task An_ordinary_visit_has_no_referral_status(string type)
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, type, priority: "routine"), Chv);

        Assert.Equal(201, reply.Status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reply.Body.GetProperty("referralStatus").ValueKind);
    }

    [Fact]
    public async Task A_teleconsult_is_a_referral_of_its_own_kind_and_also_starts_as_requested()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, "teleconsult"), Chv);

        Assert.Equal("requested", reply.Body.GetProperty("referralStatus").GetString());
    }

    [Fact]
    public async Task Sending_the_same_appointment_again_saves_it_once()
    {
        var (patient, id) = await ReferralRequestedAsync();

        var again = await PutAppointmentAsync(id, Appointment(id, patient), Chv);

        Assert.Equal(200, again.Status);
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM appointments"));
    }

    // ------------------------------------------------------------------ the referral journey

    [Fact]
    public async Task A_nurse_accepts_a_referral_and_the_system_records_who_and_when_not_the_phone()
    {
        var (patient, id) = await ReferralRequestedAsync();
        Time.Advance(TimeSpan.FromMinutes(3));

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted", version: 1) with { HandledBy = "someone-else" }, Nurse);

        Assert.Equal(200, reply.Status);
        Assert.Equal("accepted", reply.Body.GetProperty("referralStatus").GetString());
        Assert.Equal("nurse-17", reply.Body.GetProperty("handledBy").GetString());   // the signed-in nurse, not "someone-else"
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, reply.Body.GetProperty("handledAt").ValueKind);
        Assert.Equal(2, reply.Body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task The_whole_journey_requested_accepted_seen_works_and_a_doctor_may_finish_what_a_nurse_started()
    {
        var (patient, id) = await ReferralRequestedAsync();

        var accepted = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted", version: 1), Nurse);
        var seen = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "seen", version: 2, status: "completed"), Doctor);

        Assert.Equal(200, accepted.Status);
        Assert.Equal(200, seen.Status);
        Assert.Equal("seen", seen.Body.GetProperty("referralStatus").GetString());
        Assert.Equal("completed", seen.Body.GetProperty("status").GetString());
        Assert.Equal("dr-eshun", seen.Body.GetProperty("handledBy").GetString());
    }

    [Fact]
    public async Task A_referral_can_be_declined()
    {
        var (patient, id) = await ReferralRequestedAsync();

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "declined", version: 1), Doctor);

        Assert.Equal("declined", reply.Body.GetProperty("referralStatus").GetString());
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("declined")]
    [InlineData("seen")]
    public async Task A_CHV_cannot_accept_decline_or_mark_seen_a_referral(string status)
    {
        var (patient, id) = await ReferralRequestedAsync();

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: status, version: 1), Chv);

        Assert.Equal(403, reply.Status);
        Assert.Equal("only_clinicians_handle_referrals", reply.Error);
        Assert.Equal("requested", await ScalarAsync<string>("SELECT referral_status FROM appointments"));
    }

    [Fact]
    public async Task A_CHV_cannot_create_a_referral_that_is_already_accepted()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted"), Chv);

        Assert.Equal(403, reply.Status);
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM appointments"));
    }

    [Theory]
    [InlineData("accepted", "declined")]   // changed their mind: not allowed
    [InlineData("accepted", "requested")]  // a step back
    [InlineData("requested", "seen")]      // a step skipped
    [InlineData("seen", "accepted")]
    [InlineData("declined", "accepted")]
    public async Task A_referral_only_moves_forward_so_a_step_back_or_a_skipped_step_is_a_conflict(string from, string to)
    {
        var (patient, id) = await ReferralRequestedAsync();
        var version = 1;
        foreach (var step in from switch { "requested" => Array.Empty<string>(), "accepted" => ["accepted"], "declined" => ["declined"], _ => ["accepted", "seen"] })
        {
            Assert.Equal(200, (await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: step, version: version++), Nurse)).Status);
        }

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: to, version: version), Nurse);

        Assert.Equal(409, reply.Status);
        Assert.Equal(from, reply.Body.GetProperty("current").GetProperty("referralStatus").GetString());
    }

    [Theory]
    [InlineData("requested", "accepted", true)]
    [InlineData("requested", "declined", true)]
    [InlineData("accepted", "seen", true)]
    [InlineData("accepted", "accepted", true)]
    [InlineData("requested", "seen", false)]
    [InlineData("seen", "requested", false)]
    [InlineData("declined", "seen", false)]
    public void The_allowed_moves_are_exactly_these(string from, string to, bool allowed) =>
        Assert.Equal(allowed, AppointmentValidator.MayMove(from, to));

    [Fact]
    public async Task A_CHV_marking_the_visit_done_does_not_reset_a_referral_a_nurse_accepted()
    {
        var (patient, id) = await ReferralRequestedAsync();
        await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted", version: 1), Nurse);

        // The CHV's phone has pulled version 2 and marks the visit completed without saying anything about the referral.
        var reply = await PutAppointmentAsync(id, Appointment(id, patient, version: 2, status: "completed"), Chv);

        Assert.Equal(200, reply.Status);
        Assert.Equal("completed", reply.Body.GetProperty("status").GetString());
        Assert.Equal("accepted", reply.Body.GetProperty("referralStatus").GetString());
        Assert.Equal("nurse-17", reply.Body.GetProperty("handledBy").GetString());   // still the nurse's decision
    }

    // ------------------------------------------------------------------ versions

    [Fact]
    public async Task A_phone_that_missed_the_nurses_decision_gets_a_conflict_with_the_servers_version()
    {
        var (patient, id) = await ReferralRequestedAsync();
        await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted", version: 1), Nurse);

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, version: 1, status: "cancelled"), Chv);   // stale: still version 1

        Assert.Equal(409, reply.Status);
        Assert.Equal("accepted", reply.Body.GetProperty("current").GetProperty("referralStatus").GetString());
        Assert.Equal("scheduled", await ScalarAsync<string>("SELECT status FROM appointments"));
    }

    [Fact]
    public async Task An_ordinary_change_at_the_right_version_updates_the_visit()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutAppointmentAsync(id, Appointment(id, patient, "follow_up", priority: "routine"), Chv);

        var reply = await PutAppointmentAsync(id, Appointment(id, patient, "follow_up", priority: "routine", version: 1, status: "missed") with { Facility = "Ayikuma CHPS" }, Chv);

        Assert.Equal(200, reply.Status);
        Assert.Equal("missed", reply.Body.GetProperty("status").GetString());
        Assert.Equal("Ayikuma CHPS", reply.Body.GetProperty("facility").GetString());
        Assert.Equal(2, reply.Body.GetProperty("version").GetInt32());
    }

    // ------------------------------------------------------------------ what must be refused

    public static TheoryData<string, Func<AppointmentDto, AppointmentDto>> BadAppointments => new()
    {
        { "type", a => a with { Type = "surgery" } },
        { "priority", a => a with { Priority = "whenever" } },
        { "status", a => a with { Status = "pending" } },
        { "facility", a => a with { Facility = " " } },
        { "reason", a => a with { Reason = new string('x', 2001) } },
        { "scheduledAt", a => a with { ScheduledAt = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero) } },
        { "referralStatus", a => a with { ReferralStatus = "ignored" } },
        { "referralStatus", a => a with { Type = "follow_up", ReferralStatus = "requested" } },
        { "id", a => a with { Id = Guid.NewGuid().ToString() } },
        { "patientId", a => a with { PatientId = "nope" } },
        { "version", a => a with { Version = -3 } },
    };

    [Theory]
    [MemberData(nameof(BadAppointments))]
    public async Task Something_wrong_in_an_appointment_is_a_422_that_names_the_field(string field, Func<AppointmentDto, AppointmentDto> spoil)
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, spoil(Appointment(id, patient)), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError(field));
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM appointments"));
    }

    [Fact]
    public async Task The_patient_and_the_type_of_an_existing_appointment_cannot_change()
    {
        var (patient, id) = await ReferralRequestedAsync();
        var other = await SavedPatientAsync();

        var otherPatient = await PutAppointmentAsync(id, Appointment(id, other, version: 1), Chv);
        var otherType = await PutAppointmentAsync(id, Appointment(id, patient, "follow_up", version: 1), Chv);

        Assert.NotNull(otherPatient.FieldError("patientId"));
        Assert.NotNull(otherType.FieldError("type"));
    }

    [Fact]
    public async Task An_appointment_for_a_patient_who_is_not_on_the_server_yet_is_a_422_so_the_phone_tries_again()
    {
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, Guid.NewGuid()), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError("patientId"));
    }

    [Fact]
    public async Task A_CHV_cannot_book_for_a_patient_in_another_district()
    {
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, elsewhere), Chv);

        Assert.Equal(422, reply.Status);   // the CHV cannot even see that patient
    }

    [Fact]
    public async Task A_worker_switched_off_or_with_no_identity_in_the_token_is_refused()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'chv-042' RETURNING 1");

        var off = await PutAppointmentAsync(id, Appointment(id, patient), Chv);
        var nobody = await ResultReader.RunAsync(await HealthConnect.RecordsService.Features.PutAppointment.Handle(
            id, Appointment(id, patient), new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), Sessions, Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HealthConnect.RecordsService.Features.PutAppointment>.Instance,
            Events, new HealthConnect.RecordsService.Events.AppointmentEventOptions(null, "test"), CancellationToken.None));

        Assert.Equal(401, off.Status);
        Assert.Equal(401, nobody.Status);
    }

    // ------------------------------------------------------------------ what each worker sees

    [Fact]
    public async Task The_nurse_sees_the_CHVs_referral_when_she_pulls_and_the_CHV_sees_the_nurses_answer()
    {
        var (patient, id) = await ReferralRequestedAsync();

        var inbox = await PullAppointmentsAsync(Nurse);
        await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted", version: 1), Nurse);
        var back = await PullAppointmentsAsync(Chv);

        Assert.Equal(1, inbox.Body.GetArrayLength());
        Assert.Equal("requested", inbox.Body[0].GetProperty("referralStatus").GetString());
        Assert.Equal("accepted", back.Body[0].GetProperty("referralStatus").GetString());
        Assert.Equal("nurse-17", back.Body[0].GetProperty("handledBy").GetString());
    }

    [Fact]
    public async Task A_CHV_pulls_only_appointments_for_their_own_districts_patients()
    {
        await ReferralRequestedAsync();
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var other = Guid.NewGuid();
        await PutAppointmentAsync(other, Appointment(other, elsewhere), Nurse);

        var chv = await PullAppointmentsAsync(Chv);
        var nurse = await PullAppointmentsAsync(Nurse);

        Assert.Equal(1, chv.Body.GetArrayLength());
        Assert.Equal(2, nurse.Body.GetArrayLength());
    }

    [Fact]
    public async Task Pulling_appointments_after_a_moment_returns_only_what_changed_since()
    {
        var (patient, first) = await ReferralRequestedAsync();
        await Task.Delay(30);
        var middle = DateTimeOffset.UtcNow;
        await Task.Delay(30);
        await PutAppointmentAsync(first, Appointment(first, patient, referralStatus: "accepted", version: 1), Nurse);
        var second = Guid.NewGuid();
        await PutAppointmentAsync(second, Appointment(second, patient, "follow_up", priority: "routine"), Chv);

        var after = await PullAppointmentsAsync(Chv, middle);

        Assert.Equal(2, after.Body.GetArrayLength());
        Assert.Equal(first.ToString(), after.Body[0].GetProperty("id").GetString());
    }
}
