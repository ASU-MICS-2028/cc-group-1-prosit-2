using Npgsql;
using NSubstitute;

namespace HealthConnect.Data.Tests;

public class DatabaseTests
{
    private const string Arn = "arn:aws:secretsmanager:af-south-1:111111111111:secret:example";

    private static DatabaseSettings OnTheServer(string host = "healthconnect-db.example.internal", int port = 5432) =>
        new(null, host, port, "healthconnect_staging", "healthconnect", Arn, "/opt/healthconnect/rds-ca.pem", "af-south-1");

    [UnitTest]
    [Fact]
    public async Task On_a_laptop_it_connects_with_the_connection_string_it_is_given()
    {
        var settings = new DatabaseSettings("Host=localhost;Port=5433;Database=hc;Username=me;Password=pw", null, 5432, null, "x", null, null, null);
        await using var database = new Database(settings, Substitute.For<IDatabaseSecretReader>());

        var connection = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString);

        Assert.True(database.IsConfigured);
        Assert.Equal("localhost", connection.Host);
        Assert.Equal(5433, connection.Port);
        Assert.Equal("hc", connection.Database);
    }

    [UnitTest]
    [Fact]
    public async Task On_the_server_it_always_uses_TLS_and_checks_the_database_certificate()
    {
        await using var database = new Database(OnTheServer(), Substitute.For<IDatabaseSecretReader>());

        var connection = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString);

        Assert.Equal(SslMode.VerifyFull, connection.SslMode);
        Assert.Equal("/opt/healthconnect/rds-ca.pem", connection.RootCertificate);
        Assert.Equal("healthconnect-db.example.internal", connection.Host);
        Assert.Equal("healthconnect_staging", connection.Database);
        Assert.Equal("healthconnect", connection.Username);
    }

    [UnitTest]
    [Fact]
    public async Task On_the_server_it_gives_up_quickly_and_keeps_the_pool_small()
    {
        await using var database = new Database(OnTheServer(), Substitute.For<IDatabaseSecretReader>());

        var connection = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString);

        Assert.Equal(GssEncryptionMode.Disable, connection.GssEncryptionMode);
        Assert.Equal(Database.ConnectTimeoutSeconds, connection.Timeout);
        Assert.Equal(Database.CommandTimeoutSeconds, connection.CommandTimeout);
        Assert.Equal(Database.MaxPoolSize, connection.MaxPoolSize);
    }

    [UnitTest]
    [Fact]
    public async Task The_password_is_never_part_of_the_connection_string()
    {
        var secrets = Substitute.For<IDatabaseSecretReader>();
        secrets.GetPasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string>("a-very-secret-password"));
        await using var database = new Database(OnTheServer(), secrets);

        Assert.DoesNotContain("a-very-secret-password", database.DataSource.ConnectionString);
        Assert.Null(new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString).Password);
    }

    [UnitTest]
    [Fact]
    public async Task Not_configured_means_there_is_no_data_source_to_use()
    {
        var settings = new DatabaseSettings(null, null, 5432, null, "healthconnect", null, null, null);
        await using var database = new Database(settings, Substitute.For<IDatabaseSecretReader>());

        Assert.False(database.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => database.DataSource);
    }

    [UnitTest]
    [Fact]
    public async Task Disposing_before_it_was_ever_used_is_harmless()
    {
        var database = new Database(OnTheServer(), Substitute.For<IDatabaseSecretReader>());

        await database.DisposeAsync();
    }

    [IntegrationTest]
    [Fact]
    public async Task On_the_server_the_password_comes_from_the_secret_each_time_a_connection_is_made()
    {
        // Nothing listens on this port: we only need to see that Npgsql asked for the password. It asks on a background
        // task, so wait for that call to happen (up to 10 s) instead of checking at one instant, which races on a busy machine.
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secrets = Substitute.For<IDatabaseSecretReader>();
        secrets.GetPasswordAsync(Arn, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            asked.TrySetResult();
            return new ValueTask<string>("from-secret");
        });
        await using var database = new Database(OnTheServer("127.0.0.1", 1), secrets);

        await Assert.ThrowsAnyAsync<Exception>(async () => await database.DataSource.OpenConnectionAsync(CancellationToken.None));

        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
