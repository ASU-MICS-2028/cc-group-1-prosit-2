using Microsoft.Extensions.Configuration;

namespace HealthConnect.Data.Tests;

[UnitTest]
public class DatabaseSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static readonly (string, string)[] ServerSettings =
    [
        ("DB_HOST", "healthconnect-db.example.internal"),
        ("DB_NAME", "healthconnect_staging"),
        ("DB_SECRET_ARN", "arn:aws:secretsmanager:af-south-1:111111111111:secret:example"),
        ("DB_SSL_CA", "/opt/healthconnect/rds-ca.pem"),
        ("AWS_REGION", "af-south-1"),
    ];

    [Fact]
    public void Reads_the_settings_the_deploy_gives_the_container()
    {
        var settings = DatabaseSettings.From(Config(ServerSettings));

        Assert.Equal("healthconnect-db.example.internal", settings.Host);
        Assert.Equal("healthconnect_staging", settings.Name);
        Assert.Equal("arn:aws:secretsmanager:af-south-1:111111111111:secret:example", settings.SecretArn);
        Assert.Equal("/opt/healthconnect/rds-ca.pem", settings.SslCa);
        Assert.Equal("af-south-1", settings.Region);
        Assert.True(settings.IsConfigured);
        Assert.Empty(settings.Missing());
    }

    [Fact]
    public void Uses_the_database_login_and_port_the_server_is_built_with_unless_told_otherwise()
    {
        var settings = DatabaseSettings.From(Config(ServerSettings));

        Assert.Equal("healthconnect", settings.User);
        Assert.Equal(5432, settings.Port);
    }

    [Fact]
    public void Takes_a_different_login_and_port_when_given()
    {
        var settings = DatabaseSettings.From(Config([.. ServerSettings, ("DB_USER", "app"), ("DB_PORT", "6543")]));

        Assert.Equal("app", settings.User);
        Assert.Equal(6543, settings.Port);
    }

    [Fact]
    public void Ignores_a_port_that_is_not_a_number()
    {
        var settings = DatabaseSettings.From(Config([.. ServerSettings, ("DB_PORT", "abc")]));

        Assert.Equal(5432, settings.Port);
    }

    [Fact]
    public void A_connection_string_is_enough_on_a_laptop()
    {
        var settings = DatabaseSettings.From(Config(("ConnectionStrings:Default", "Host=localhost;Database=x")));

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void Nothing_set_means_not_configured()
    {
        var settings = DatabaseSettings.From(Config());

        Assert.False(settings.IsConfigured);
        Assert.Equal(["DB_HOST", "DB_NAME", "DB_SECRET_ARN", "DB_SSL_CA"], settings.Missing());
    }

    [Theory]
    [InlineData("DB_HOST")]
    [InlineData("DB_NAME")]
    [InlineData("DB_SECRET_ARN")]
    [InlineData("DB_SSL_CA")]
    public void One_missing_server_setting_means_not_configured_and_names_it(string missing)
    {
        var settings = DatabaseSettings.From(Config(ServerSettings.Where(s => s.Item1 != missing).ToArray()));

        Assert.False(settings.IsConfigured);
        Assert.Equal([missing], settings.Missing());
    }

    [Fact]
    public void A_blank_setting_counts_as_missing()
    {
        var settings = DatabaseSettings.From(Config(ServerSettings.Select(s => s.Item1 == "DB_HOST" ? ("DB_HOST", "  ") : s).ToArray()));

        Assert.False(settings.IsConfigured);
        Assert.Equal(["DB_HOST"], settings.Missing());
    }
}
