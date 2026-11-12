using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HealthConnect.RecordsService.Payments;

/// <summary>
/// Consultation fees (brief, week 2: "payment integration for consultation fees via mobile money"). Paid through Paystack, which offers
/// MTN MoMo, Telecel Cash and AirtelTigo Money in Ghana on its own checkout page, so the app never handles a PIN or a card.
///   Paystack:SecretKey   from the environment's secret healthconnect/&lt;env&gt;/paystack (PaystackSecret); without it payments are off
///                        and a teleconsult call needs no payment (local runs and tests).
///   PAYMENT_FEE_PESEWAS  the fee, default 2000 (GHS 20).
/// </summary>
public sealed record PaymentSettings(string? SecretKey, int FeePesewas)
{
    public const int DefaultFeePesewas = 2000;

    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);

    public static PaymentSettings From(IConfiguration configuration)
    {
        var fee = int.TryParse(configuration["PAYMENT_FEE_PESEWAS"], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : DefaultFeePesewas;
        return new PaymentSettings(configuration["Paystack:SecretKey"], fee);
    }

    /// <summary>
    /// Paystack signs each webhook with HMAC-SHA512 of the raw body, keyed with the secret key, in x-paystack-signature (hex).
    /// Compared in constant time, so a forged webhook can't be timed into shape.
    /// </summary>
    public bool IsSignedByPaystack(ReadOnlySpan<byte> body, string? signature)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(SecretKey!), body);
        byte[] given;
        try
        {
            given = Convert.FromHexString(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}

/// <summary>A checkout Paystack opened: the page the phone goes to.</summary>
public sealed record PaystackCheckout(string AuthorizationUrl, string AccessCode);

/// <summary>A transaction as Paystack reports it. Status is Paystack's: success, failed, abandoned, ongoing, pending, ...</summary>
public sealed record PaystackTransaction(string Reference, string Status, int Amount, string Currency, string? Channel, long? Id, DateTimeOffset? PaidAt);

public interface IPaystack
{
    Task<PaystackCheckout> StartAsync(string reference, int amountPesewas, string email, string callbackUrl, Guid appointmentId, CancellationToken cancellationToken);

    /// <summary>Asks Paystack itself what happened to a payment: null when it has never heard of the reference.</summary>
    Task<PaystackTransaction?> CheckAsync(string reference, CancellationToken cancellationToken);
}

public sealed class PaystackException(string message) : Exception(message);

/// <summary>Paystack's REST API: POST /transaction/initialize and GET /transaction/verify/{reference}.</summary>
public sealed class PaystackClient(HttpClient http, PaymentSettings settings, ILogger<PaystackClient> logger) : IPaystack
{
    public const string HttpClientName = "paystack";
    public static readonly Uri BaseAddress = new("https://api.paystack.co/");

    public async Task<PaystackCheckout> StartAsync(
        string reference, int amountPesewas, string email, string callbackUrl, Guid appointmentId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "transaction/initialize");
        request.Content = JsonContent.Create(new
        {
            email,
            amount = amountPesewas,
            currency = "GHS",
            reference,
            callback_url = callbackUrl,
            channels = new[] { "mobile_money", "card" },
            metadata = new { appointment_id = appointmentId },
        });
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await Read(response, cancellationToken);
        if (!response.IsSuccessStatusCode || body?.Data is not { } data
            || data.GetProperty("authorization_url").GetString() is not { Length: > 0 } url)
        {
            logger.LogWarning("Paystack refused to start payment {Reference}: {Status} {Message}", reference, (int)response.StatusCode, body?.Message);
            throw new PaystackException("Paystack did not start the payment.");
        }

        return new PaystackCheckout(url, data.GetProperty("access_code").GetString() ?? "");
    }

    public async Task<PaystackTransaction?> CheckAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"transaction/verify/{Uri.EscapeDataString(reference)}");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await Read(response, cancellationToken);
        if (!response.IsSuccessStatusCode || body?.Data is not { } data)
        {
            if (body?.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            logger.LogWarning("Paystack could not check payment {Reference}: {Status} {Message}", reference, (int)response.StatusCode, body?.Message);
            throw new PaystackException("Paystack could not be asked about the payment.");
        }

        return FromJson(data);
    }

    /// <summary>The data object of a verify reply or a charge.success webhook.</summary>
    public static PaystackTransaction FromJson(JsonElement data) => new(
        data.GetProperty("reference").GetString() ?? "",
        data.GetProperty("status").GetString() ?? "",
        data.GetProperty("amount").GetInt32(),
        data.GetProperty("currency").GetString() ?? "",
        data.TryGetProperty("channel", out var channel) && channel.ValueKind == JsonValueKind.String ? channel.GetString() : null,
        data.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : null,
        data.TryGetProperty("paid_at", out var paid) && paid.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(paid.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null);

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SecretKey);
        return request;
    }

    private static async Task<Reply?> Read(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Reply>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Reply(
        [property: JsonPropertyName("status")] bool Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] JsonElement? Data);
}
