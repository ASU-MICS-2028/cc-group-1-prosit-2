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
}

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
    /// Which event a save is, or null when nothing a consumer cares about changed (a referral accepted, a reason reworded).
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
}

/// <summary>Where events go: the environment's SNS topic (APPOINTMENT_TOPIC_ARN). No topic (a laptop, the tests): nothing is published.</summary>
public sealed record AppointmentEventOptions(string? TopicArn, string Environment);

/// <summary>Used when no topic is configured.</summary>
public sealed class NoAppointmentEvents : IAppointmentEvents
{
    public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken) => Task.CompletedTask;
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

    public async Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var reply = await sns.Value.PublishAsync(new PublishRequest
            {
                TopicArn = options.TopicArn,
                Message = JsonSerializer.Serialize(appointmentEvent, Json),
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["eventType"] = Text(appointmentEvent.Type),
                    ["appointmentType"] = Text(appointmentEvent.Appointment.Type),
                    ["priority"] = Text(appointmentEvent.Appointment.Priority),
                },
            }, timeout.Token);
            LogPublished(logger, appointmentEvent.Type, appointmentEvent.Appointment.Id, reply.MessageId);
        }
        catch (Exception error) when (error is AmazonSimpleNotificationServiceException or OperationCanceledException or HttpRequestException
                                      or Amazon.Runtime.AmazonClientException)
        {
            // The appointment is saved; only its reminder is at risk. The warning (and the CloudWatch alarm on it) is how we find out.
            LogNotPublished(logger, error, appointmentEvent.Type, appointmentEvent.Appointment.Id);
        }
    }

    private static MessageAttributeValue Text(string value) => new() { DataType = "String", StringValue = value };

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} for appointment {AppointmentId} (SNS message {MessageId})")]
    private static partial void LogPublished(ILogger logger, string eventType, string appointmentId, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish {EventType} for appointment {AppointmentId}; the appointment is saved but gets no reminder")]
    private static partial void LogNotPublished(ILogger logger, Exception error, string eventType, string appointmentId);
}
