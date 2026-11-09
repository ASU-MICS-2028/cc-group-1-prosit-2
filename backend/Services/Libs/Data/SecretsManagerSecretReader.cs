using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace HealthConnect.Data;

/// <summary>
/// Reads the password from the secret RDS keeps for the database (a JSON text with "username" and "password").
/// It uses the server's own AWS role, so there is no key or password in any file. The AWS client is made on first
/// use, so the API can start (and its OpenAPI file can be built) on a machine with no AWS setup.
/// </summary>
public sealed class SecretsManagerSecretReader(Func<IAmazonSecretsManager> clientFactory) : IDatabaseSecretReader
{
    private readonly Lazy<IAmazonSecretsManager> _client = new(clientFactory);

    /// <summary>The normal way to make the reader: the client uses the AWS region from the settings, else the server's default.</summary>
    public static SecretsManagerSecretReader For(DatabaseSettings settings) => new(() =>
        string.IsNullOrWhiteSpace(settings.Region)
            ? new AmazonSecretsManagerClient()
            : new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(settings.Region)));

    public async ValueTask<string> GetPasswordAsync(string secretArn, CancellationToken cancellationToken)
    {
        var response = await _client.Value.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretArn }, cancellationToken);
        return ParsePassword(response.SecretString);
    }

    /// <summary>Takes the password out of the secret's JSON. Throws without echoing the secret if it is not what we expect.</summary>
    internal static string ParsePassword(string? secretString)
    {
        if (string.IsNullOrWhiteSpace(secretString))
        {
            throw new InvalidOperationException("The database secret is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(secretString);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("password", out var password)
                && password.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(password.GetString()))
            {
                return password.GetString()!;
            }
        }
        catch (JsonException)
        {
            // fall through to the same message: never include the secret's text in an error
        }

        throw new InvalidOperationException("The database secret has no \"password\" field.");
    }
}
