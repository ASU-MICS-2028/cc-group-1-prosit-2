using System.Net.Http.Json;
using System.Text.Json;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HealthConnect.SharedLibrary.Sms;

/// <summary>Why a text could not be sent. The message never holds the API key, the text, or a whole phone number.</summary>
public sealed class SmsDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Sends the text through Arkesel (arkesel.com, a Ghanaian SMS provider). Switched on only by Sms:Provider = arkesel; without it
/// the app keeps logging codes instead. Settings (never in a file in the repo): Sms:Arkesel:ApiKey from the server's environment
/// (Sms__Arkesel__ApiKey, ideally filled from AWS Secrets Manager) and Sms:Arkesel:SenderId, the name the text comes from, which
/// Arkesel may need to approve first. The key is sent in a header, never in the web address, so it does not end up in logs.
///   POST https://sms.arkesel.com/api/v2/sms/send   header api-key   { sender, message, recipients: ["233..."] }
/// Any problem (no settings, no network, Arkesel refusing, no balance) throws <see cref="SmsDeliveryException"/>; the sign-in
/// endpoint turns that into 503 sms_unavailable and does not count the code against the person's hourly limit.
/// </summary>
public sealed partial class ArkeselSmsSender(HttpClient http, IConfiguration configuration, ILogger<ArkeselSmsSender> logger) : ISmsSender
{
    public const string Provider = "arkesel";
    public const string DefaultUrl = "https://sms.arkesel.com/api/v2/sms/send";

    public async Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        var key = configuration["Sms:Arkesel:ApiKey"];
        var sender = configuration["Sms:Arkesel:SenderId"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(sender))
        {
            throw new SmsDeliveryException("Arkesel is switched on but Sms:Arkesel:ApiKey or Sms:Arkesel:SenderId is not set.");
        }

        var url = configuration["Sms:Arkesel:Url"] is { Length: > 0 } custom ? custom : DefaultUrl;
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { sender, message, recipients = new[] { to.E164.TrimStart('+') } }),
        };
        request.Headers.Add("api-key", key);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception problem) when (problem is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, to.Masked);
            throw new SmsDeliveryException("Arkesel could not be reached.", problem);
        }

        using (response)
        {
            var status = await ReadStatusAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode || status is "error" or "failed" or "fail")
            {
                LogRefused(logger, to.Masked, (int)response.StatusCode, status ?? "none");
                throw new SmsDeliveryException($"Arkesel refused the message (HTTP {(int)response.StatusCode}).");
            }

            LogSent(logger, to.Masked);
        }
    }

    /// <summary>The "status" word of Arkesel's JSON answer, lower case, if it has one. The text of the answer is never kept or logged.</summary>
    private static async Task<string?> ReadStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                    ? status.GetString()?.ToLowerInvariant()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS sent through Arkesel to {Phone}")]
    private static partial void LogSent(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Error, Message = "Arkesel refused the SMS to {Phone}: HTTP {Status}, status word {Word}")]
    private static partial void LogRefused(ILogger logger, string phone, int status, string word);

    [LoggerMessage(Level = LogLevel.Error, Message = "Arkesel could not be reached to send an SMS to {Phone}")]
    private static partial void LogUnreachable(ILogger logger, string phone);
}
