using HealthConnect.AuthService.Tests.Support;

namespace HealthConnect.AuthService.Tests;

/// <summary>Sign-in step 1: asking for a code.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class RequestCodeTests(AuthDatabase database) : AuthTestBase(database)
{
    [Fact]
    public async Task A_registered_worker_gets_a_six_digit_code_by_text()
    {
        await AddWorkerAsync();

        var reply = await RequestCodeAsync("+233200000101");

        Assert.Equal(204, reply.Status);
        var text = Assert.Single(Sms.Sent);
        Assert.Equal("+233200000101", text.Phone);
        Assert.Matches(@"HealthConnect code: \d{6}\. It works for 10 minutes\.", text.Message);
    }

    [Theory]
    [InlineData("0200000101")]
    [InlineData("200000101")]
    [InlineData("233200000101")]
    [InlineData("+233 20 000 0101")]
    [InlineData("020-000-0101")]
    public async Task The_number_may_be_typed_the_ways_people_type_it(string typed)
    {
        await AddWorkerAsync();

        var reply = await RequestCodeAsync(typed);

        Assert.Equal(204, reply.Status);
        Assert.Equal("+233200000101", Sms.Sent.Single().Phone);
    }

    [Fact]
    public async Task The_code_is_kept_only_as_a_hash_that_expires_in_ten_minutes()
    {
        await AddWorkerAsync();

        await RequestCodeAsync("+233200000101");

        var stored = Assert.Single(await StoredCodesAsync());
        Assert.NotEqual(Sms.LastCode, stored.CodeHash);
        Assert.DoesNotContain(Sms.LastCode, stored.CodeHash);
        Assert.Matches("^[0-9a-f]{64}$", stored.CodeHash);
        Assert.Equal(Start, stored.CreatedAt);
        Assert.Equal(Start.AddMinutes(10), stored.ExpiresAt);
        Assert.Equal(0, stored.Attempts);
        Assert.Null(stored.UsedAt);
    }

    [Theory]
    [InlineData("+233201111111")]   // well formed, but nobody registered it
    public async Task A_number_that_is_not_a_registered_worker_is_a_404_and_gets_no_text(string phone)
    {
        await AddWorkerAsync();

        var reply = await RequestCodeAsync(phone);

        Assert.Equal(404, reply.Status);
        Assert.Equal("unknown_number", reply.Error);
        Assert.Empty(Sms.Sent);
        Assert.Empty(await StoredCodesAsync());
    }

    [Fact]
    public async Task A_worker_who_was_switched_off_is_treated_as_unknown()
    {
        await AddWorkerAsync(active: false);

        var reply = await RequestCodeAsync("+233200000101");

        Assert.Equal(404, reply.Status);
        Assert.Empty(Sms.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("12345")]
    [InlineData("+44 7700 900123")]   // not Ghana
    public async Task Something_that_is_not_a_Ghana_number_is_a_422_that_names_the_field(string? phone)
    {
        var reply = await RequestCodeAsync(phone);

        Assert.Equal(422, reply.Status);
        Assert.Equal("Enter a Ghana mobile number.", reply.FieldError("phone"));
        Assert.Empty(Sms.Sent);
    }

    [Fact]
    public async Task Asking_again_straight_away_is_refused_until_the_wait_is_over()
    {
        await AddWorkerAsync();
        await RequestCodeAsync("+233200000101");

        Time.Advance(TimeSpan.FromSeconds(10));
        var tooSoon = await RequestCodeAsync("+233200000101");
        Time.Advance(TimeSpan.FromSeconds(21));
        var later = await RequestCodeAsync("+233200000101");

        Assert.Equal(429, tooSoon.Status);
        Assert.Equal("too_many_requests", tooSoon.Error);
        Assert.Equal(204, later.Status);
        Assert.Equal(2, Sms.Sent.Count);
    }

    [Fact]
    public async Task Only_six_codes_an_hour_go_to_one_number()
    {
        await AddWorkerAsync();
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(204, (await RequestCodeAsync("+233200000101")).Status);
            Time.Advance(TimeSpan.FromSeconds(40));
        }

        var seventh = await RequestCodeAsync("+233200000101");
        Time.Advance(TimeSpan.FromHours(1));
        var nextHour = await RequestCodeAsync("+233200000101");

        Assert.Equal(429, seventh.Status);
        Assert.Equal(204, nextHour.Status);
    }

    [Fact]
    public async Task One_workers_limit_does_not_hold_up_another()
    {
        await AddWorkerAsync();
        await AddWorkerAsync("nurse-17", "+233200000102", "nurse");
        await RequestCodeAsync("+233200000101");

        var other = await RequestCodeAsync("+233200000102");

        Assert.Equal(204, other.Status);
    }

    [Fact]
    public async Task If_the_text_cannot_be_sent_it_says_so_and_the_failed_code_does_not_count_against_the_limit()
    {
        await AddWorkerAsync();
        Sms.Fail = true;

        var reply = await RequestCodeAsync("+233200000101");
        Sms.Fail = false;
        var retry = await RequestCodeAsync("+233200000101");   // straight away: the cooldown must not be used up by the failure

        Assert.Equal(503, reply.Status);
        Assert.Equal("sms_unavailable", reply.Error);
        Assert.Equal(204, retry.Status);
        Assert.Single(await StoredCodesAsync());
    }
}
