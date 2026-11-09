using Microsoft.Extensions.Configuration;

namespace HealthConnect.Data;

/// <summary>
/// Where the database is and how to log in. There are two ways to say it:
/// <list type="bullet">
/// <item>On the server, the environment variables the deploy sets: DB_HOST, DB_NAME, DB_SECRET_ARN (the AWS secret that holds
/// the password), DB_SSL_CA (the certificate file that proves the database is ours), AWS_REGION. No password is ever in a file.</item>
/// <item>On a laptop or in tests, one ordinary connection string: ConnectionStrings:Default.</item>
/// </list>
/// </summary>
public sealed record DatabaseSettings(
    string? ConnectionString,
    string? Host,
    int Port,
    string? Name,
    string User,
    string? SecretArn,
    string? SslCa,
    string? Region)
{
    /// <summary>The database login the RDS instance was created with (deploy/terraform/database.tf).</summary>
    public const string DefaultUser = "healthconnect";

    public const int DefaultPort = 5432;

    /// <summary>True when there is enough to connect: a connection string, or every setting the server needs.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString)
        || (Present(Host) && Present(Name) && Present(SecretArn) && Present(SslCa));

    public static DatabaseSettings From(IConfiguration configuration) => new(
        configuration.GetConnectionString("Default"),
        configuration["DB_HOST"],
        int.TryParse(configuration["DB_PORT"], out var port) ? port : DefaultPort,
        configuration["DB_NAME"],
        Present(configuration["DB_USER"]) ? configuration["DB_USER"]! : DefaultUser,
        configuration["DB_SECRET_ARN"],
        configuration["DB_SSL_CA"],
        configuration["AWS_REGION"]);

    /// <summary>The names of the server settings that are missing, for a clear start-up message.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        AddIfMissing(missing, Host, "DB_HOST");
        AddIfMissing(missing, Name, "DB_NAME");
        AddIfMissing(missing, SecretArn, "DB_SECRET_ARN");
        AddIfMissing(missing, SslCa, "DB_SSL_CA");
        return missing;
    }

    private static void AddIfMissing(List<string> missing, string? value, string name)
    {
        if (!Present(value))
        {
            missing.Add(name);
        }
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
}
