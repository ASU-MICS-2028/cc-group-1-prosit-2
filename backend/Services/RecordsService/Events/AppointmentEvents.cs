using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using HealthConnect.RecordsService.Models;
using Microsoft.Extensions.Logging;

namespace HealthConnect.RecordsService.Events;

/// <summary>
/// What happened to an appointment, published for the serverless consumers (Lab 7): the SMS reminder and the specialist
/// notification. The API does not know who listens; it publishes one event and moves on.
/// </summary>
public static class AppointmentEventTypes
{
    public const string Created = "appointment.created";
    public const string Rescheduled = "appointment.rescheduled";
    public const string Cancelled = "appointment.cancelled";

    /// <summary>Anything else a reminder cares about: marked done or missed, or moved to another facility.</summary>
    public const string Updated = "appointment.updated";

    /// <summary>A nurse or doctor accepted or declined a referral or teleconsult: the patient is told, and an emergency's escalation stops.</summary>
    public const string ReferralAnswered = "appointment.referral_answered";
}

/// <summary>Events about care other than visits, published to the same topic so the same two consumers handle them.</summary>
public static class CareEventTypes
{
    /// <summary>A doctor wrote a prescription: the patient gets its code by SMS to take to a pharmacy.</summary>
    public const string PrescriptionCreated = "prescription.created";

    /// <summary>A test came back not normal: the clinicians are told to look at it.</summary>
    public const string ResultAbnormal = "result.abnormal";
}

/// <summary>
/// A prescription or test result event. Like an appointment event it never carries the patient's name; the phone and language
/// are there only for a text to the patient (a prescription), and are left out when the clinicians are the audience (a result).
/// </summary>
public sealed record CareEvent(
    string EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string Environment,
    CarePrescription? Prescription,
    CareResult? Result,
    AppointmentEventPatient Patient)
{
    public static CareEvent ForPrescription(PrescriptionDto p, string? phone, string? language, string environment, DateTimeOffset now) => new(
        $"{CareEventTypes.PrescriptionCreated}:{p.Id}",
        CareEventTypes.PrescriptionCreated,
        now,
        environment,
        new CarePrescription(p.Id, p.Code, p.Items.Count, p.PrescriberName),
        null,
        new AppointmentEventPatient(phone, language));

    public static CareEvent ForAbnormalResult(TestResultDto r, string environment, DateTimeOffset now) => new(
        $"{CareEventTypes.ResultAbnormal}:{r.Id}:v{r.Version}",
        CareEventTypes.ResultAbnormal,
        now,
        environment,
        null,
        new CareResult(r.Id, r.Category, r.Test, r.Facility, r.ResultedAt),
        new AppointmentEventPatient(null, null));
}

public sealed record CarePrescription(string Id, string Code, int Medicines, string? Prescriber);

public sealed record CareResult(string Id, string Category, string Test, string? Facility, DateTimeOffset? ResultedAt);

/// <summary>
/// One event. The id is the type, the appointment and its version, so a repeat of the same change has the same id and the
/// consumers can drop it. It carries the patient's phone (for the reminder text) but never their name.
/// </summary>
public sealed record AppointmentEvent(
    string EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string Environment,
    AppointmentEventBody Appointment,
    AppointmentEventPatient Patient)
{
    public static AppointmentEvent For(string type, AppointmentDto saved, string? phone, string? language, string environment, DateTimeOffset now) => new(
        $"{type}:{saved.Id}:v{saved.Version}",
        type,
        now,
        environment,
        new AppointmentEventBody(saved.Id, saved.Type, saved.ScheduledAt.ToUniversalTime(), saved.Facility, saved.Priority, saved.Status, saved.ReferralStatus, saved.Version),
        new AppointmentEventPatient(phone, language));

    /// <summary>
    /// Which event a save is, or null when nothing a consumer cares about changed (a referral marked seen, a reason reworded).
    /// </summary>
    public static string? TypeOf(AppointmentDto? before, AppointmentDto after)
    {
        if (before is null)
        {
            return AppointmentEventTypes.Created;
        }

        if (before.Status != after.Status)
        {
            return after.Status == "cancelled" ? AppointmentEventTypes.Cancelled : AppointmentEventTypes.Updated;
        }

        if (before.ScheduledAt != after.ScheduledAt)
        {
            return AppointmentEventTypes.Rescheduled;
        }

        if (before.ReferralStatus != after.ReferralStatus && after.ReferralStatus is "accepted" or "declined")
        {
            return AppointmentEventTypes.ReferralAnswered;
        }

        return before.Facility != after.Facility ? AppointmentEventTypes.Updated : null;
    }
}

public sealed record AppointmentEventBody(
    string Id, string Type, DateTimeOffset ScheduledAt, string Facility, string Priority, string Status, string? ReferralStatus, int Version);

public sealed record AppointmentEventPatient(string? Phone, string? PreferredLanguage);

/// <summary>Publishes appointment events. Publishing never fails a save: the appointment is already stored.</summary>
public interface IAppointmentEvents
{
    Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken);

    /// <summary>A prescription or result event, to the same topic. Like an appointment event, it never fails the save.</summary>
    Task PublishAsync(CareEvent careEvent, CancellationToken cancellationToken);
}

/// <summary>Where events go: the environment's SNS topic (APPOINTMENT_TOPIC_ARN). No topic (a laptop, the tests): nothing is published.</summary>
public sealed record AppointmentEventOptions(string? TopicArn, string Environment);

/// <summary>Used when no topic is configured.</summary>
public sealed class NoAppointmentEvents : IAppointmentEvents
{
    public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishAsync(CareEvent careEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Sends events to SNS, which copies each one to every consumer's queue. The message attributes let a consumer's
/// subscription take only what it needs (specialist-notify takes only referrals and teleconsults).
/// </summary>
public sealed partial class SnsAppointmentEvents(Lazy<IAmazonSimpleNotificationService> sns, AppointmentEventOptions options, ILogger<SnsAppointmentEvents> logger)
    : IAppointmentEvents
{
    /// <summary>A slow SNS must not hold up the health worker's save.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken) =>
        SendAsync(
            JsonSerializer.Serialize(appointmentEvent, Json),
            new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = Text(appointmentEvent.Type),
                ["appointmentType"] = Text(appointmentEvent.Appointment.Type),
                ["priority"] = Text(appointmentEvent.Appointment.Priority),
            },
            appointmentEvent.Type,
            appointmentEvent.Appointment.Id,
            cancellationToken);

    public Task PublishAsync(CareEvent careEvent, CancellationToken cancellationToken) =>
        SendAsync(
            JsonSerializer.Serialize(careEvent, Json),
            new Dictionary<string, MessageAttributeValue> { ["eventType"] = Text(careEvent.Type) },
            careEvent.Type,
            careEvent.Prescription?.Id ?? careEvent.Result?.Id ?? string.Empty,
            cancellationToken);

    private async Task SendAsync(
        string message, Dictionary<string, MessageAttributeValue> attributes, string type, string recordId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var reply = await sns.Value.PublishAsync(
                new PublishRequest { TopicArn = options.TopicArn, Message = message, MessageAttributes = attributes }, timeout.Token);
            LogPublished(logger, type, recordId, reply.MessageId);
        }
        catch (Exception error) when (error is AmazonSimpleNotificationServiceException or OperationCanceledException or HttpRequestException
                                      or Amazon.Runtime.AmazonClientException)
        {
            // The record is saved; only its text or alert is at risk. The warning (and the CloudWatch alarm on it) is how we find out.
            LogNotPublished(logger, error, type, recordId);
        }
    }

    private static MessageAttributeValue Text(string value) => new() { DataType = "String", StringValue = value };

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} for record {RecordId} (SNS message {MessageId})")]
    private static partial void LogPublished(ILogger logger, string eventType, string recordId, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish {EventType} for record {RecordId}; the record is saved but gets no text or alert")]
    private static partial void LogNotPublished(ILogger logger, Exception error, string eventType, string recordId);
}
