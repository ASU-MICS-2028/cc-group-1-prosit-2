using System.Security.Claims;
using HealthConnect.AuthService.Providers.Implementations;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.AuthService.Tests.Support;
using HealthConnect.Data;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace HealthConnect.AuthService.Tests;

[UnitTest]
public sealed class CodeGeneratorTests
{
    [Fact]
    public void A_code_is_always_exactly_six_digits_including_ones_that_start_with_zero()
    {
        var generator = new CodeGenerator();

        var codes = Enumerable.Range(0, 5000).Select(_ => generator.NewCode()).ToList();

        Assert.All(codes, code => Assert.Matches("^[0-9]{6}$", code));
        Assert.Contains(codes, code => code.StartsWith('0'));   // 1 in 10 codes: leading zeros are kept
    }

    [Fact]
    public void The_codes_are_not_a_pattern()
    {
        var generator = new CodeGenerator();

        var distinct = Enumerable.Range(0, 1000).Select(_ => generator.NewCode()).Distinct().Count();

        Assert.True(distinct > 950, $"only {distinct} different codes in 1000");
    }
}

[UnitTest]
public sealed class CodeHasherTests
{
    private static readonly PhoneNumber Ama = Parse("+233200000101");
    private static readonly PhoneNumber Kofi = Parse("+233200000102");

    private static PhoneNumber Parse(string value)
    {
        Assert.True(PhoneNumber.TryParse(value, out var phone));
        return phone;
    }

    private static CodeHasher HasherWith(string key) =>
        new(new SigningKeyProvider(
            new Database(new DatabaseSettings(null, null, 5432, null, "x", null, null, null), Substitute.For<IDatabaseSecretReader>()),
            Options.Create(new AuthOptions { SigningKey = key })));

    [Fact]
    public async Task A_hash_is_sixty_four_hex_characters_and_never_contains_the_code()
    {
        var hash = await HasherWith(new string('a', 40)).HashAsync(Ama, "123456", CancellationToken.None);

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain("123456", hash);
    }

    [Fact]
    public async Task The_same_code_and_number_always_give_the_same_hash()
    {
        var hasher = HasherWith(new string('a', 40));

        Assert.Equal(
            await hasher.HashAsync(Ama, "123456", CancellationToken.None),
            await hasher.HashAsync(Ama, "123456", CancellationToken.None));
    }

    [Fact]
    public async Task A_different_number_a_different_code_or_a_different_key_gives_a_different_hash()
    {
        var hasher = HasherWith(new string('a', 40));
        var baseline = await hasher.HashAsync(Ama, "123456", CancellationToken.None);

        Assert.NotEqual(baseline, await hasher.HashAsync(Kofi, "123456", CancellationToken.None));
        Assert.NotEqual(baseline, await hasher.HashAsync(Ama, "123457", CancellationToken.None));
        Assert.NotEqual(baseline, await HasherWith(new string('b', 40)).HashAsync(Ama, "123456", CancellationToken.None));
    }

    [Fact]
    public async Task A_guess_matches_only_its_own_hash()
    {
        var hasher = HasherWith(new string('a', 40));
        var stored = await hasher.HashAsync(Ama, "123456", CancellationToken.None);

        Assert.True(await hasher.MatchesAsync(Ama, "123456", stored, CancellationToken.None));
        Assert.False(await hasher.MatchesAsync(Ama, "654321", stored, CancellationToken.None));
        Assert.False(await hasher.MatchesAsync(Kofi, "123456", stored, CancellationToken.None));
        Assert.False(await hasher.MatchesAsync(Ama, "123456", "not-a-hash", CancellationToken.None));
        Assert.False(await hasher.MatchesAsync(Ama, "123456", string.Empty, CancellationToken.None));
    }
}

[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class SigningKeyProviderTests(AuthDatabase database) : AuthTestBase(database)
{
    private SigningKeyProvider From(string? configured) =>
        new(Database.NewDatabase(), Options.Create(new AuthOptions { SigningKey = configured }));

    [Fact]
    public async Task A_key_from_settings_is_used_as_it_is_and_the_database_is_not_touched()
    {
        var key = await From(new string('s', 50)).GetAsync(CancellationToken.None);

        Assert.Equal(new string('s', 50), System.Text.Encoding.UTF8.GetString(key.Key));
        Assert.Empty(await RowsInAuthKeysAsync());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("31 characters is one too few!!!")]
    public async Task A_key_from_settings_that_is_too_short_is_refused(string tooShort)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await From(tooShort).GetAsync(CancellationToken.None));

        Assert.DoesNotContain(tooShort, error.Message);
    }

    [Fact]
    public async Task With_no_key_in_settings_the_first_start_makes_a_random_key_in_the_database()
    {
        var key = await From(null).GetAsync(CancellationToken.None);

        Assert.Equal(32, key.Key.Length);
        Assert.Equal(key.Key, Assert.Single(await RowsInAuthKeysAsync()));
    }

    [Fact]
    public async Task Every_restart_and_every_container_gets_the_same_key()
    {
        var first = await From(null).GetAsync(CancellationToken.None);

        var restarted = await From(null).GetAsync(CancellationToken.None);

        Assert.Equal(first.Key, restarted.Key);
    }

    [Fact]
    public async Task Many_containers_starting_together_still_agree_on_one_key()
    {
        var keys = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ => (await From(null).GetAsync(CancellationToken.None)).Key));

        Assert.Single(keys.Select(Convert.ToHexString).Distinct());
        Assert.Single(await RowsInAuthKeysAsync());
    }

    [Fact]
    public async Task The_key_is_remembered_so_token_checking_never_waits_on_the_database()
    {
        var provider = From(null);
        Assert.Null(provider.Cached);

        await provider.GetAsync(CancellationToken.None);

        Assert.NotNull(provider.Cached);
    }

    [Fact]
    public async Task A_failure_to_load_the_key_is_not_remembered_so_the_next_call_tries_again()
    {
        var nowhere = new Database(new DatabaseSettings(null, null, 5432, null, "x", null, null, null), Substitute.For<IDatabaseSecretReader>());
        var provider = new SigningKeyProvider(nowhere, Options.Create(new AuthOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetAsync(CancellationToken.None));

        Assert.Null(provider.Cached);
    }

    private Task<List<byte[]>> RowsInAuthKeysAsync() => Database.AuthKeysAsync();
}

[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class JwtTokenIssuerTests(AuthDatabase database) : AuthTestBase(database)
{
    private async Task<(string Token, DateTimeOffset Expires)> IssueAsync(string role = "nurse")
    {
        var worker = await AddWorkerAsync("nurse-17", "+233200000102", role);
        var token = await Tokens().IssueAsync(worker, CancellationToken.None);
        return (token.Value, token.ExpiresAt);
    }

    private async Task<TokenValidationResult> ValidateAsync(string token, string? keyText = null, DateTimeOffset? at = null)
    {
        var key = keyText is null ? await Keys().GetAsync(CancellationToken.None) : new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(keyText));
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = Settings.Issuer,
            ValidAudience = Settings.Audience,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (_, expires, _, _) => expires is { } end && end >= (at ?? Time.GetUtcNow()).UtcDateTime,
        };
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
    }

    [Fact]
    public async Task The_token_says_who_the_worker_is()
    {
        var (token, _) = await IssueAsync("doctor");

        var result = await ValidateAsync(token);

        Assert.True(result.IsValid);
        var claims = result.ClaimsIdentity;
        Assert.Equal("nurse-17", claims.FindFirst("sub")?.Value);
        Assert.Equal("Synthetic nurse-17", claims.FindFirst("name")?.Value);
        Assert.Equal("doctor", claims.FindFirst("role")?.Value);
        Assert.Equal("Shai-Osudoku", claims.FindFirst("district")?.Value);
        Assert.False(string.IsNullOrEmpty(claims.FindFirst(ClaimTypes.Role)?.Value) && claims.FindFirst("role") is null);
    }

    [Fact]
    public async Task The_token_lasts_seven_days_from_now()
    {
        var (_, expires) = await IssueAsync();

        Assert.Equal(Start.AddDays(7), expires);
    }

    [Fact]
    public async Task A_token_stops_working_when_it_expires()
    {
        var (token, expires) = await IssueAsync();

        Assert.True((await ValidateAsync(token, at: expires.AddSeconds(-1))).IsValid);
        Assert.False((await ValidateAsync(token, at: expires.AddSeconds(1))).IsValid);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var (token, _) = await IssueAsync();

        Assert.False((await ValidateAsync(token, keyText: new string('x', 48))).IsValid);
    }

    [Fact]
    public async Task A_token_that_was_edited_is_refused()
    {
        var (token, _) = await IssueAsync("nurse");
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1])).Replace("\"nurse\"", "\"admin\"");
        var forged = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        Assert.False((await ValidateAsync(forged)).IsValid);
    }

    [Fact]
    public async Task Two_tokens_for_the_same_worker_are_different()
    {
        var worker = await AddWorkerAsync();

        var first = await Tokens().IssueAsync(worker, CancellationToken.None);
        var second = await Tokens().IssueAsync(worker, CancellationToken.None);

        Assert.NotEqual(first.Value, second.Value);
    }
}
