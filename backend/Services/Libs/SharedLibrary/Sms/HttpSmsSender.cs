using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HealthConnect.SharedLibrary.Sms;

/// <summary>
/// Sends the text through httpSMS (httpsms.com): an Android phone running the httpSMS app sends it from its own SIM, so a text
/// costs what that SIM's bundle charges. Switched on by Sms:Provider = httpsms. Settings (from this environment's SMS secret,
/// see <see cref="SmsSecret"/>): Sms:HttpSms:ApiKey, Sms:HttpSms:From (the phone's number) and, recommended,
/// Sms:HttpSms:EncryptionKey, the passphrase set in the phone's app. With it the text is end-to-end encrypted, so httpSMS's
/// servers only see the recipient's number, never the code or the reminder.
///   POST https://api.httpsms.com/v1/messages/send   header x-api-key   { from, to, content, encrypted }
/// Any problem throws <see cref="SmsDeliveryException"/>, which never holds the key, the text or the whole number.
/// </summary>
public sealed partial class HttpSmsSender(HttpClient http, IConfiguration configuration, ILogger<HttpSmsSender> logger) : ISmsSender
{
    public const string Provider = "httpsms";
    public const string DefaultUrl = "https://api.httpsms.com/v1/messages/send";

    public async Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        var key = configuration["Sms:HttpSms:ApiKey"];
        var from = configuration["Sms:HttpSms:From"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(from))
        {
            throw new SmsDeliveryException("httpSMS is switched on but Sms:HttpSms:ApiKey or Sms:HttpSms:From is not set.");
        }

        var passphrase = configuration["Sms:HttpSms:EncryptionKey"];
        var encrypted = !string.IsNullOrEmpty(passphrase);
        var url = configuration["Sms:HttpSms:Url"] is { Length: > 0 } custom ? custom : DefaultUrl;
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { from, to = to.E164, content = encrypted ? Encrypt(passphrase!, message) : message, encrypted }),
        };
        request.Headers.Add("x-api-key", key);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception problem) when (problem is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, to.Masked);
            throw new SmsDeliveryException("httpSMS could not be reached.", problem);
        }

        using (response)
        {
            var status = await ReadStatusAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode || status is "error" or "failed" or "fail")
            {
                LogRefused(logger, to.Masked, (int)response.StatusCode, status ?? "none");
                throw new SmsDeliveryException($"httpSMS refused the message (HTTP {(int)response.StatusCode}).");
            }

            LogQueued(logger, to.Masked, encrypted);
        }
    }

    /// <summary>
    /// httpSMS's end-to-end format (its own clients' CipherService): base64(IV ‖ AES-256-CFB128(SHA-256(passphrase), IV, text)).
    /// CFB is a stream mode, so the text is zero-padded to whole blocks for .NET and the cipher text cut back to its length.
    /// </summary>
    public static string Encrypt(string passphrase, string text, byte[]? iv = null)
    {
        iv ??= RandomNumberGenerator.GetBytes(16);
        var plain = Encoding.UTF8.GetBytes(text);
        var padded = new byte[(plain.Length + 15) / 16 * 16];
        plain.CopyTo(padded, 0);
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
        var cipher = aes.EncryptCfb(padded, iv, PaddingMode.None, feedbackSizeInBits: 128);
        return Convert.ToBase64String([.. iv, .. cipher.AsSpan(0, plain.Length)]);
    }

    /// <summary>The reverse of <see cref="Encrypt"/>; used by the tests to read back what the phone would decrypt.</summary>
    public static string Decrypt(string passphrase, string payload)
    {
        var raw = Convert.FromBase64String(payload);
        var cipher = raw.AsSpan(16).ToArray();
        var padded = new byte[(cipher.Length + 15) / 16 * 16];
        cipher.CopyTo(padded, 0);
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
        var plain = aes.DecryptCfb(padded, raw.AsSpan(0, 16).ToArray(), PaddingMode.None, feedbackSizeInBits: 128);
        return Encoding.UTF8.GetString(plain, 0, cipher.Length);
    }

    /// <summary>The "status" word of httpSMS's JSON answer, lower case, if it has one. The answer itself is never kept or logged.</summary>
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

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS to {Phone} queued on httpSMS (end-to-end encrypted: {Encrypted})")]
    private static partial void LogQueued(ILogger logger, string phone, bool encrypted);

    [LoggerMessage(Level = LogLevel.Error, Message = "httpSMS refused the SMS to {Phone}: HTTP {Status}, status word {Word}")]
    private static partial void LogRefused(ILogger logger, string phone, int status, string word);

    [LoggerMessage(Level = LogLevel.Error, Message = "httpSMS could not be reached to send an SMS to {Phone}")]
    private static partial void LogUnreachable(ILogger logger, string phone);
}
