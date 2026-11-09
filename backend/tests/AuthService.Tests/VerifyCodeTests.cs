using HealthConnect.AuthService.Features;
using HealthConnect.AuthService.Models;
using HealthConnect.AuthService.Providers.Implementations;
using HealthConnect.AuthService.Tests.Support;
using HealthConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthConnect.AuthService.Tests;

/// <summary>Sign-in step 2: trading the code for a token.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class VerifyCodeTests(AuthDatabase database) : AuthTestBase(database)
{
    private async Task<string> RequestedCodeAsync(string phone = "+233200000101")
    {
        await RequestCodeAsync(phone);
        return Sms.LastCode;
    }

    private async Task<LoginCode> OnlyCodeAsync() => Assert.Single(await StoredCodesAsync());

    // ------------------------------------------------------------------ the good path

    [Fact]
    public async Task The_right_code_signs_the_worker_in_with_the_answer_the_app_expects()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();

        var reply = await VerifyAsync("+233200000101", code);

        Assert.Equal(200, reply.Status);
        Assert.NotEmpty(reply.Body.GetProperty("token").GetString()!);
        Assert.Equal(Start.AddDays(7), reply.Body.GetProperty("expiresAt").GetDateTimeOffset());
        var user = reply.Body.GetProperty("user");
        Assert.Equal("chv-042", user.GetProperty("id").GetString());
        Assert.Equal("Synthetic chv-042", user.GetProperty("name").GetString());
        Assert.Equal("chv", user.GetProperty("role").GetString());
        Assert.Equal("Shai-Osudoku", user.GetProperty("district").GetString());
    }

    [Fact]
    public async Task The_token_is_good_for_seven_days_and_says_who_it_is_for()
    {
        await AddWorkerAsync(role: "nurse");
        var code = await RequestedCodeAsync();

        var reply = await VerifyAsync("0200000101", code);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(reply.Body.GetProperty("token").GetString());
        Assert.Equal("chv-042", token.Subject);
        Assert.Equal("nurse", token.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("Shai-Osudoku", token.Claims.Single(c => c.Type == "district").Value);
        Assert.Equal(Start.AddDays(7).UtcDateTime, token.ValidTo);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();
        Assert.Equal(200, (await VerifyAsync("+233200000101", code)).Status);

        var again = await VerifyAsync("+233200000101", code);

        Assert.Equal(401, again.Status);
        Assert.NotNull((await OnlyCodeAsync()).UsedAt);
    }

    [Fact]
    public async Task Only_the_newest_code_counts()
    {
        await AddWorkerAsync();
        var first = await RequestedCodeAsync();
        Time.Advance(TimeSpan.FromMinutes(1));
        var second = await RequestedCodeAsync();

        var oldOne = await VerifyAsync("+233200000101", first);
        var newOne = await VerifyAsync("+233200000101", second);

        Assert.Equal(401, oldOne.Status);
        Assert.Equal(200, newOne.Status);
    }

    // ------------------------------------------------------------------ wrong, late, too many

    [Fact]
    public async Task A_wrong_code_is_a_401_and_is_counted()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();
        var wrong = code == "000000" ? "111111" : "000000";

        var reply = await VerifyAsync("+233200000101", wrong);

        Assert.Equal(401, reply.Status);
        Assert.Equal("wrong_code", reply.Error);
        Assert.Equal(1, (await OnlyCodeAsync()).Attempts);
        Assert.Null((await OnlyCodeAsync()).UsedAt);
    }

    [Fact]
    public async Task After_five_wrong_tries_even_the_right_code_is_refused()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(401, (await VerifyAsync("+233200000101", wrong)).Status);
        }

        var sixth = await VerifyAsync("+233200000101", wrong);
        var rightButLate = await VerifyAsync("+233200000101", code);

        Assert.Equal(429, sixth.Status);
        Assert.Equal("too_many_attempts", sixth.Error);
        Assert.Equal(429, rightButLate.Status);
        Assert.Equal(5, (await OnlyCodeAsync()).Attempts);
    }

    [Fact]
    public async Task The_fifth_try_can_still_be_right()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 4; i++)
        {
            await VerifyAsync("+233200000101", wrong);
        }

        var reply = await VerifyAsync("+233200000101", code);

        Assert.Equal(200, reply.Status);
    }

    [Fact]
    public async Task A_code_stops_working_after_ten_minutes()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();

        Time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        var justInTime = await VerifyAsync("+233200000101", code);
        Assert.Equal(200, justInTime.Status);

        await RequestCodeAsync("+233200000101");   // a second code, left to go stale
        var second = Sms.LastCode;
        Time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var tooLate = await VerifyAsync("+233200000101", second);

        Assert.Equal(401, tooLate.Status);
        Assert.Equal("code_expired", tooLate.Error);
    }

    [Fact]
    public async Task Without_asking_for_a_code_first_there_is_nothing_to_verify()
    {
        await AddWorkerAsync();

        var reply = await VerifyAsync("+233200000101", "123456");

        Assert.Equal(401, reply.Status);
        Assert.Equal("code_expired", reply.Error);
    }

    [Fact]
    public async Task A_code_for_one_number_does_not_work_for_another()
    {
        await AddWorkerAsync();
        await AddWorkerAsync("nurse-17", "+233200000102", "nurse");
        var code = await RequestedCodeAsync("+233200000101");

        var reply = await VerifyAsync("+233200000102", code);

        Assert.Equal(401, reply.Status);
    }

    // ------------------------------------------------------------------ who is asking

    [Fact]
    public async Task An_unknown_number_gets_the_same_401_as_a_wrong_code_so_it_reveals_nothing()
    {
        await AddWorkerAsync();

        var unknown = await VerifyAsync("+233201111111", "123456");

        Assert.Equal(401, unknown.Status);
        Assert.Equal("wrong_code", unknown.Error);
    }

    [Fact]
    public async Task A_worker_switched_off_after_asking_for_a_code_cannot_sign_in()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();
        await using (var db = Database.NewContext())
        {
            await db.Users.Where(u => u.Id == "chv-042").ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false));
        }

        var reply = await VerifyAsync("+233200000101", code);

        Assert.Equal(401, reply.Status);
    }

    [Theory]
    [InlineData("+233200000101", null)]
    [InlineData("+233200000101", "")]
    [InlineData("+233200000101", "12345")]
    [InlineData("+233200000101", "1234567")]
    [InlineData("+233200000101", "12ab56")]
    [InlineData("+233200000101", "١٢٣٤٥٦")]   // Arabic-Indic digits are not the digits we sent
    public async Task A_code_that_is_not_six_digits_is_a_422_that_names_the_field(string phone, string? code)
    {
        var reply = await VerifyAsync(phone, code);

        Assert.Equal(422, reply.Status);
        Assert.Equal("Enter the 6-digit code.", reply.FieldError("code"));
    }

    [Fact]
    public async Task A_bad_number_and_a_bad_code_are_both_named()
    {
        var reply = await VerifyAsync("nope", "abc");

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError("phone"));
        Assert.NotNull(reply.FieldError("code"));
    }

    [Fact]
    public async Task Spaces_around_the_code_are_forgiven()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();

        var reply = await VerifyAsync("+233200000101", $" {code} ");

        Assert.Equal(200, reply.Status);
    }

    // ------------------------------------------------------------------ the fixed demo code

    [Fact]
    public async Task The_demo_code_signs_in_a_demo_worker_without_asking_for_a_text()
    {
        Demo.Enabled = true;
        await AddWorkerAsync(demo: true);

        var reply = await VerifyAsync("+233200000101", "123456");

        Assert.Equal(200, reply.Status);
        Assert.Equal("chv-042", reply.Body.GetProperty("user").GetProperty("id").GetString());
        Assert.Empty(Sms.Sent);
        Assert.Empty(await StoredCodesAsync());
    }

    [Fact]
    public async Task The_demo_code_never_works_for_a_real_worker()
    {
        Demo.Enabled = true;
        await AddWorkerAsync(demo: false);

        var reply = await VerifyAsync("+233200000101", "123456");

        Assert.Equal(401, reply.Status);
    }

    [Fact]
    public async Task The_demo_code_does_nothing_when_demo_mode_is_switched_off()
    {
        Demo.Enabled = false;
        await AddWorkerAsync(demo: true);

        var reply = await VerifyAsync("+233200000101", "123456");

        Assert.Equal(401, reply.Status);
    }

    [Fact]
    public async Task Only_the_configured_demo_code_works_not_any_code()
    {
        Demo.Enabled = true;
        Demo.Code = "424242";
        await AddWorkerAsync(demo: true);

        Assert.Equal(401, (await VerifyAsync("+233200000101", "123456")).Status);
        Assert.Equal(200, (await VerifyAsync("+233200000101", "424242")).Status);
    }

    [Fact]
    public async Task A_demo_worker_can_also_sign_in_with_a_real_text_code()
    {
        Demo.Enabled = true;
        await AddWorkerAsync(demo: true);
        var code = await RequestedCodeAsync();

        Assert.Equal(200, (await VerifyAsync("+233200000101", code)).Status);
    }

    // ------------------------------------------------------------------ two at once

    [Fact]
    public async Task Two_requests_with_the_same_right_code_at_the_same_moment_sign_in_only_one()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();

        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => VerifyAsync("+233200000101", code)));

        Assert.Equal(1, replies.Count(r => r.Status == 200));
        Assert.Equal(5, replies.Count(r => r.Status == 401));
    }

    [Fact]
    public async Task Wrong_guesses_made_at_the_same_moment_are_all_counted()
    {
        await AddWorkerAsync();
        var code = await RequestedCodeAsync();
        var wrong = code == "000000" ? "111111" : "000000";

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => VerifyAsync("+233200000101", wrong)));

        Assert.Equal(4, (await OnlyCodeAsync()).Attempts);
    }

    [Fact]
    public async Task The_verify_handler_is_wired_to_read_only_the_options_it_needs()
    {
        // Calling the handler with the real option objects, as the API's dependency injection does.
        await AddWorkerAsync();
        await using var db = Database.NewContext();

        var result = await VerifyCode.Handle(
            new VerifyCodeRequest("+233200000101", "000000"), db, Hasher(), Tokens(), Time,
            Options.Create(new AuthOptions { SigningKey = Settings.SigningKey }), Options.Create(new DemoOptions()),
            NullLogger<VerifyCode>.Instance, CancellationToken.None);

        Assert.Equal(401, (await ResultReader.RunAsync(result)).Status);
        _ = new CodeGenerator();
    }
}
