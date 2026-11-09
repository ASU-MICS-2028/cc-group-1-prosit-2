namespace HealthConnect.Data;

/// <summary>Reads the database password from where it is kept safe (AWS Secrets Manager on the server).</summary>
public interface IDatabaseSecretReader
{
    ValueTask<string> GetPasswordAsync(string secretArn, CancellationToken cancellationToken);
}
