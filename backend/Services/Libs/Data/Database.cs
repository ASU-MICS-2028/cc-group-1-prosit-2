using Npgsql;

namespace HealthConnect.Data;

/// <summary>
/// The one door to PostgreSQL. It makes the connection pool on first use (so starting the API never waits on the database)
/// and, on the server, always connects with TLS and checks the database's certificate (sslmode=verify-full).
/// </summary>
public sealed class Database(DatabaseSettings settings, IDatabaseSecretReader secrets) : IAsyncDisposable
{
    /// <summary>How long to wait for a connection, so a broken network turns /health red quickly instead of hanging.</summary>
    public const int ConnectTimeoutSeconds = 5;

    public const int CommandTimeoutSeconds = 15;

    /// <summary>The pool is small on purpose: the database is a db.t4g.micro shared by production and staging.</summary>
    public const int MaxPoolSize = 20;

    /// <summary>How often the password is read again, so a password rotation in Secrets Manager needs no restart.</summary>
    public static readonly TimeSpan PasswordRefresh = TimeSpan.FromMinutes(30);

    /// <summary>After a failed read (AWS hiccup), try again soon.</summary>
    public static readonly TimeSpan PasswordRetry = TimeSpan.FromSeconds(10);

    private readonly Lazy<NpgsqlDataSource> _dataSource = new(() => Create(settings, secrets));

    public bool IsConfigured => settings.IsConfigured;

    public NpgsqlDataSource DataSource => settings.IsConfigured
        ? _dataSource.Value
        : throw new InvalidOperationException("The database is not configured.");

    public async ValueTask DisposeAsync()
    {
        if (_dataSource.IsValueCreated)
        {
            await _dataSource.Value.DisposeAsync();
        }
    }

    internal static NpgsqlDataSource Create(DatabaseSettings settings, IDatabaseSecretReader secrets)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            // A laptop or a test: the connection string says everything.
            return new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Name,
            Username = settings.User,
            SslMode = SslMode.VerifyFull,
            RootCertificate = settings.SslCa,
            Timeout = ConnectTimeoutSeconds,
            CommandTimeout = CommandTimeoutSeconds,
            MaxPoolSize = MaxPoolSize,
            // We log in with a password over TLS, never Kerberos. Without this Npgsql tries to load a Kerberos library
            // the slim Alpine image does not have and logs an error on every new connection.
            GssEncryptionMode = GssEncryptionMode.Disable,
        };

        var builder = new NpgsqlDataSourceBuilder(connection.ConnectionString);
        builder.UsePeriodicPasswordProvider(
            (_, cancellationToken) => secrets.GetPasswordAsync(settings.SecretArn!, cancellationToken),
            PasswordRefresh,
            PasswordRetry);
        return builder.Build();
    }
}
