using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using HealthConnect.RecordsService.Events;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace HealthConnect.RecordsService.Tests;

/// <summary>Lab 7: a saved appointment is published once, as the right event, for the reminder and specialist functions.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class AppointmentEventTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private const string Phone = "+233241234567";

    private async Task<Guid> PatientWithPhoneAsync()
    {
        var id = Guid.NewGuid();
        var reply = await PutPatientAsync(id, Patient(id) with { Phone = Phone }, RecordsDatabase.Chv);
        Assert.Equal(201, reply.Status);
        return id;
    }

    [Fact]
    public async Task A_new_visit_is_published_as_created_with_the_patients_phone_but_not_their_name()
    {
        var patient = await PatientWithPhoneAsync();
        var id = Guid.NewGuid();

        await PutAppointmentAsync(id, Appointment(id, patient, "follow_up", priority: "routine"), RecordsDatabase.Chv);

        var published = Assert.Single(Events.Published);
        Assert.Equal(AppointmentEventTypes.Created, published.Type);
        Assert.Equal($"appointment.created:{id}:v1", published.EventId);
        Assert.Equal(Phone, published.Patient.Phone);
        Assert.Equal("tw", published.Patient.PreferredLanguage);
        Assert.Equal("follow_up", published.Appointment.Type);
        Assert.Equal(Start.AddDays(1), published.Appointment.ScheduledAt);
        Assert.Equal("test", published.Environment);
        Assert.DoesNotContain("Ama", System.Text.Json.JsonSerializer.Serialize(published));
    }

    [Fact]
    public async Task Moving_a_visit_is_published_as_rescheduled()
    {
        var patient = await PatientWithPhoneAsync();
        var id = Guid.NewGuid();
        await PutAppointmentAsync(id, Appointment(id, patient, "follow_up"), RecordsDatabase.Chv);

        await PutAppointmentAsync(id, Appointment(id, patient, "follow_up", version: 1) with { ScheduledAt = Start.AddDays(3) }, RecordsDatabase.Chv);

        Assert.Equal([AppointmentEventTypes.Created, AppointmentEventTypes.Rescheduled], Events.Published.Select(e => e.Type));
        Assert.Equal($"appointment.rescheduled:{id}:v2", Events.Published[1].EventId);
    }

    [Fact]
    public async Task Cancelling_is_published_as_cancelled_and_marking_done_as_updated()
    {
        var patient = await PatientWithPhoneAsync();
        var cancelled = Guid.NewGuid();
        var done = Guid.NewGuid();
        await PutAppointmentAsync(cancelled, Appointment(cancelled, patient, "follow_up"), RecordsDatabase.Chv);
        await PutAppointmentAsync(done, Appointment(done, patient, "follow_up"), RecordsDatabase.Chv);

        await PutAppointmentAsync(cancelled, Appointment(cancelled, patient, "follow_up", version: 1, status: "cancelled"), RecordsDatabase.Chv);
        await PutAppointmentAsync(done, Appointment(done, patient, "follow_up", version: 1, status: "completed"), RecordsDatabase.Chv);

        Assert.Equal(AppointmentEventTypes.Cancelled, Events.Published[2].Type);
        Assert.Equal(AppointmentEventTypes.Updated, Events.Published[3].Type);
        Assert.Equal("completed", Events.Published[3].Appointment.Status);
    }

    [Fact]
    public async Task The_same_save_sent_twice_by_a_retrying_phone_is_published_once()
    {
        var patient = await PatientWithPhoneAsync();
        var id = Guid.NewGuid();

        var first = await PutAppointmentAsync(id, Appointment(id, patient), RecordsDatabase.Chv);
        var again = await PutAppointmentAsync(id, Appointment(id, patient), RecordsDatabase.Chv);

        Assert.Equal(201, first.Status);
        Assert.Equal(200, again.Status);
        Assert.Single(Events.Published);
    }

    [Fact]
    public async Task A_nurse_accepting_a_referral_changes_nothing_a_reminder_cares_about_so_nothing_is_published()
    {
        var patient = await PatientWithPhoneAsync();
        var id = Guid.NewGuid();
        await PutAppointmentAsync(id, Appointment(id, patient), RecordsDatabase.Chv);

        var accepted = await PutAppointmentAsync(id, Appointment(id, patient, referralStatus: "accepted", version: 1), RecordsDatabase.Nurse);

        Assert.Equal(200, accepted.Status);
        Assert.Single(Events.Published);
    }

    [Fact]
    public async Task A_refused_save_publishes_nothing()
    {
        var id = Guid.NewGuid();

        var reply = await PutAppointmentAsync(id, Appointment(id, Guid.NewGuid()), RecordsDatabase.Chv);   // no such patient

        Assert.Equal(422, reply.Status);
        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task A_patient_without_a_phone_is_still_published_with_no_phone()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        await PutAppointmentAsync(id, Appointment(id, patient, "antenatal", priority: "routine"), RecordsDatabase.Chv);

        Assert.Null(Assert.Single(Events.Published).Patient.Phone);
    }
}

/// <summary>The SNS side, without a database.</summary>
public sealed class SnsAppointmentEventsTests
{
    private static AppointmentEvent Sample() => AppointmentEvent.For(
        AppointmentEventTypes.Created,
        new AppointmentDto(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "teleconsult", DateTimeOffset.UtcNow.AddDays(1), "Kintampo Municipal Hospital",
            "Cough", "emergency", "scheduled", "requested", null, null, null, null, 1),
        "+233241234567", "en", "staging", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Publishes_to_the_topic_with_the_attributes_the_subscriptions_filter_on()
    {
        var sns = Substitute.For<IAmazonSimpleNotificationService>();
        sns.PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>()).Returns(new PublishResponse { MessageId = "m-1" });
        var events = new SnsAppointmentEvents(new Lazy<IAmazonSimpleNotificationService>(sns), new AppointmentEventOptions("arn:topic", "staging"),
            NullLogger<SnsAppointmentEvents>.Instance);

        await events.PublishAsync(Sample(), CancellationToken.None);

        await sns.Received(1).PublishAsync(
            Arg.Is<PublishRequest>(r =>
                r.TopicArn == "arn:topic"
                && r.MessageAttributes["eventType"].StringValue == "appointment.created"
                && r.MessageAttributes["appointmentType"].StringValue == "teleconsult"
                && r.MessageAttributes["priority"].StringValue == "emergency"
                && r.Message.Contains("\"eventId\":\"appointment.created:")
                && r.Message.Contains("\"scheduledAt\"")
                && !r.Message.Contains("Cough")),   // the reason (clinical text) is not in the event
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_SNS_outage_does_not_fail_the_save()
    {
        var sns = Substitute.For<IAmazonSimpleNotificationService>();
        sns.PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new AmazonSimpleNotificationServiceException("down"));
        var events = new SnsAppointmentEvents(new Lazy<IAmazonSimpleNotificationService>(sns), new AppointmentEventOptions("arn:topic", "staging"),
            NullLogger<SnsAppointmentEvents>.Instance);

        var error = await Record.ExceptionAsync(() => events.PublishAsync(Sample(), CancellationToken.None));

        Assert.Null(error);
    }

    [Fact]
    public async Task No_topic_means_nothing_is_published()
    {
        var error = await Record.ExceptionAsync(() => new NoAppointmentEvents().PublishAsync(Sample(), CancellationToken.None));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("scheduled", "scheduled", 0, "Dodowa", null)]
    [InlineData("scheduled", "cancelled", 0, "Dodowa", AppointmentEventTypes.Cancelled)]
    [InlineData("scheduled", "missed", 0, "Dodowa", AppointmentEventTypes.Updated)]
    [InlineData("scheduled", "scheduled", 2, "Dodowa", AppointmentEventTypes.Rescheduled)]
    [InlineData("scheduled", "scheduled", 0, "Kintampo", AppointmentEventTypes.Updated)]
    public void Which_event_a_change_is(string before, string after, int daysMoved, string facility, string? expected)
    {
        var at = DateTimeOffset.UtcNow;
        AppointmentDto Dto(string status, DateTimeOffset when, string where) =>
            new("a", "p", "follow_up", when, where, null, "routine", status, null, null, null, null, null, 1);

        Assert.Equal(expected, AppointmentEvent.TypeOf(Dto(before, at, "Dodowa"), Dto(after, at.AddDays(daysMoved), facility)));
        Assert.Equal(AppointmentEventTypes.Created, AppointmentEvent.TypeOf(null, Dto(after, at, facility)));
    }
}
