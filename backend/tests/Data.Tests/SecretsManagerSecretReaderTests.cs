using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using NSubstitute;

namespace HealthConnect.Data.Tests;

[UnitTest]
public class SecretsManagerSecretReaderTests
{
    private const string Arn = "arn:aws:secretsmanager:af-south-1:111111111111:secret:example";

    [Fact]
    public void Takes_the_password_out_of_the_secret_RDS_keeps()
    {
        var password = SecretsManagerSecretReader.ParsePassword("""{"username":"healthconnect","password":"s3cret-value"}""");

        Assert.Equal("s3cret-value", password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all, hunter2")]
    [InlineData("{}")]
    [InlineData("""{"password":""}""")]
    [InlineData("""{"password":5}""")]
    [InlineData("""["hunter2"]""")]
    public void Refuses_a_secret_that_has_no_usable_password_without_repeating_it(string? secret)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SecretsManagerSecretReader.ParsePassword(secret));

        Assert.DoesNotContain("hunter2", error.Message);
    }

    [Fact]
    public async Task Asks_AWS_for_the_secret_by_its_ARN_and_returns_the_password()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = """{"username":"healthconnect","password":"from-aws"}""" });
        var reader = new SecretsManagerSecretReader(() => aws);

        var password = await reader.GetPasswordAsync(Arn, CancellationToken.None);

        Assert.Equal("from-aws", password);
        await aws.Received(1).GetSecretValueAsync(
            Arg.Is<GetSecretValueRequest>(r => r.SecretId == Arn), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_create_the_AWS_client_until_the_first_read()
    {
        var created = 0;
        var aws = Substitute.For<IAmazonSecretsManager>();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = """{"password":"x"}""" });
        var reader = new SecretsManagerSecretReader(() =>
        {
            created++;
            return aws;
        });

        Assert.Equal(0, created);
        await reader.GetPasswordAsync(Arn, CancellationToken.None);
        await reader.GetPasswordAsync(Arn, CancellationToken.None);
        Assert.Equal(1, created);
    }

    [Theory]
    [InlineData("af-south-1")]
    [InlineData(null)]
    public void The_normal_way_to_make_the_reader_needs_no_AWS_setup_until_it_is_used(string? region)
    {
        var settings = new DatabaseSettings(null, "h", 5432, "d", "u", Arn, "/ca.pem", region);

        var reader = SecretsManagerSecretReader.For(settings);

        Assert.NotNull(reader);
    }
}
