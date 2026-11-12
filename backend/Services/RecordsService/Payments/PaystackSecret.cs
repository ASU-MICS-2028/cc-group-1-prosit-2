using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace HealthConnect.RecordsService.Payments;

/// <summary>
/// Payments are switched on per environment by one secret in AWS Secrets Manager, healthconnect/&lt;env&gt;/paystack, named by
/// PAYSTACK_SECRET_ID (deploy/compose.yml): { "secretKey": "sk_...", "publicKey": "pk_..." }. At start-up it becomes Paystack:* settings.
/// No secret, or one the server may not read yet: payments stay off and the API still starts. Read with the server's own AWS role,
/// so the key is never in a file, an image or a container setting.
/// </summary>
public static class PaystackSecret
{
    public const string SecretIdVariable = "PAYSTACK_SECRET_ID";

    public static IConfigurationBuilder AddPaystackSecret(this IConfigurationBuilder builder) =>
        builder.AddPaystackSecret(Environment.GetEnvironmentVariable(SecretIdVariable), ReadFromAws);

    public static IConfigurationBuilder AddPaystackSecret(this IConfigurationBuilder builder, string? secretId, Func<string, string?> read)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return builder;
        }

        string? json;
        try
        {
            json = read(secretId);
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Paystack secret {secretId} could not be read ({problem.GetType().Name}); payments are off.");
            return builder;
        }

        return json is null ? builder : builder.AddInMemoryCollection(ToSettings(json));
    }

    /// <summary>The secret's JSON as Paystack:* settings. Throws without echoing the secret when it is not what we expect.</summary>
    public static Dictionary<string, string?> ToSettings(string json)
    {
        Dictionary<string, string?> values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The Paystack secret is not a JSON object of text values.");
        }

        var key = values.GetValueOrDefault("secretKey");
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("sk_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Paystack secret needs a \"secretKey\" starting with sk_.");
        }

        return new() { ["Paystack:SecretKey"] = key, ["Paystack:PublicKey"] = values.GetValueOrDefault("publicKey") };
    }

    private static string? ReadFromAws(string secretId)
    {
        using var client = new AmazonSecretsManagerClient();
        try
        {
            return client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId }).GetAwaiter().GetResult().SecretString;
        }
        catch (ResourceNotFoundException)
        {
            return null; // payments not switched on for this environment yet
        }
    }
}
