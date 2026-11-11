using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace HealthConnect.SharedLibrary.Sms;

/// <summary>
/// Real texts are switched on per environment by creating one secret by hand in AWS Secrets Manager, healthconnect/&lt;env&gt;/sms.
/// The reminder function reads the same secret, so one switch turns on both sign-in codes and reminders. Its name comes from
/// SMS_SECRET_ID (set by deploy/compose.yml). It is a JSON object:
///   { "provider": "httpsms", "apiKey": "...", "from": "+233...", "encryptionKey": "..." }   or
///   { "provider": "arkesel", "apiKey": "...", "senderId": "HlthConnect" }
/// At start-up it becomes the Sms:* settings the senders read. No SMS_SECRET_ID, or no such secret yet: nothing changes and
/// texts are only logged. The server reads it with its own AWS role, so the keys are never in a file or a container setting.
/// </summary>
public static class SmsSecret
{
    public const string SecretIdVariable = "SMS_SECRET_ID";

    /// <summary>Adds the secret named by SMS_SECRET_ID, read with the server's AWS role.</summary>
    public static IConfigurationBuilder AddSmsSecret(this IConfigurationBuilder builder) =>
        builder.AddSmsSecret(Environment.GetEnvironmentVariable(SecretIdVariable), ReadFromAws);

    /// <summary>Adds the secret <paramref name="secretId"/>, read by <paramref name="read"/> (null when it does not exist).</summary>
    public static IConfigurationBuilder AddSmsSecret(this IConfigurationBuilder builder, string? secretId, Func<string, string?> read)
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
            // Not allowed to read it (permissions not applied yet), no network, ...: the API must still start. Texts stay
            // logged-only, as with no secret, and this line in the log says why. The message never holds the secret.
            Console.Error.WriteLine($"SMS secret {secretId} could not be read ({problem.GetType().Name}); texts are only logged.");
            return builder;
        }

        // A secret that exists but is malformed still stops start-up (ToSettings throws): the deploy rolls back to the
        // running release, and whoever created the secret sees it at once instead of codes silently not arriving.
        return json is null ? builder : builder.AddInMemoryCollection(ToSettings(json));
    }

    /// <summary>The secret's JSON as Sms:* settings. Throws without echoing the secret when it is not what we expect.</summary>
    public static Dictionary<string, string?> ToSettings(string json)
    {
        Dictionary<string, string?> values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The SMS secret is not a JSON object of text values.");
        }

        string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
        var provider = (Get("provider") ?? "log").Trim().ToLowerInvariant();
        return provider switch
        {
            HttpSmsSender.Provider => new()
            {
                ["Sms:Provider"] = provider,
                ["Sms:HttpSms:ApiKey"] = Get("apiKey"),
                ["Sms:HttpSms:From"] = Get("from"),
                ["Sms:HttpSms:EncryptionKey"] = Get("encryptionKey"),
            },
            ArkeselSmsSender.Provider => new()
            {
                ["Sms:Provider"] = provider,
                ["Sms:Arkesel:ApiKey"] = Get("apiKey"),
                ["Sms:Arkesel:SenderId"] = Get("senderId") ?? "HlthConnect",
            },
            "log" => new() { ["Sms:Provider"] = "log" },
            _ => throw new InvalidOperationException("The SMS secret's \"provider\" must be httpsms, arkesel or log."),
        };
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
            return null; // texts not switched on for this environment yet
        }
    }
}
