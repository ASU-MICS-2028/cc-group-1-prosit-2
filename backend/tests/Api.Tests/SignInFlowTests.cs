using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HealthConnect.SharedLibrary.Sms;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace HealthConnect.Api.Tests;

/// <summary>An SMS sender that remembers the last text, so a test can read the code the person would have received.</summary>
public sealed class CapturedSms : ISmsSender
{
    public string? Last { get; private set; }

    public string Code => System.Text.RegularExpressions.Regex.Match(Last!, @"\d{6}").Value;

    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        Last = message;
        return Task.CompletedTask;
    }
}

/// <summary>
/// One real PostgreSQL and one running API shared by every sign-in test (starting them for each test took minutes). A test may move the
/// shared clock forward (a token stays valid relative to when it was issued) but must put back anything else it changes.
/// </summary>
public sealed class SignInApi : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));

    public CapturedSms Sms { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        Factory = NewFactory();
        Client = Factory.CreateClient();
        await ApiFactory.HealthWhenReadyAsync(Client);
        await WaitForDemoWorkersAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await Postgres.DisposeAsync();
    }

    public ApiFactory NewFactory(Dictionary<string, string>? extra = null)
    {
        // A high limit here: one test client makes dozens of sign-ins a minute. The test that checks the limit sets its own low one.
        var settings = new Dictionary<string, string> { ["Demo:Enabled"] = "true", ["Auth:RateLimitPerMinute"] = "10000" };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return new ApiFactory(
            Postgres.ConnectionString,
            settings: settings,
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(Time);
                services.AddSingleton<ISmsSender>(Sms);
            });
    }

    /// <summary>The API says it is healthy as soon as the migrations are done, a moment before it has added the demo workers.</summary>
    public async Task WaitForDemoWorkersAsync()
    {
        await using var source = NpgsqlDataSource.Create(Postgres.ConnectionString);
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            await using var count = source.CreateCommand("SELECT count(*) FROM users WHERE demo");
            if ((long)(await count.ExecuteScalarAsync())! >= 5)
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("The demo workers were never added.");
    }
}

/// <summary>Signing in through the real HTTP API, with the real database, the way the app does.</summary>
[IntegrationTest]
public sealed class SignInFlowTests(SignInApi api) : IClassFixture<SignInApi>
{
    private FakeTimeProvider _time => api.Time;

    private CapturedSms _sms => api.Sms;

    private HttpClient _client => api.Client;

    private PostgresFixture _postgres => api.Postgres;

    private ApiFactory NewFactory(Dictionary<string, string>? extra = null) => api.NewFactory(extra);

    private static HttpRequestMessage Get(string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private async Task<JsonElement> SignInAsync(string phone, string code)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/verify", new { phone, code }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
    }

    private async Task<string> TokenAsync(string phone) => (await SignInAsync(phone, "123456")).GetProperty("token").GetString()!;

    // ------------------------------------------------------------------ the whole flow, as the app does it

    [Fact]
    public async Task A_worker_signs_in_with_the_text_code_and_uses_the_token()
    {
        var ask = await _client.PostAsJsonAsync("/api/auth/code", new { phone = "020 000 0101" }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, ask.StatusCode);

        var session = await SignInAsync("+233200000101", _sms.Code);

        Assert.Equal(["token", "expiresAt", "user"], session.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["id", "name", "role", "district"], session.GetProperty("user").EnumerateObject().Select(p => p.Name));
        Assert.Equal("chv", session.GetProperty("user").GetProperty("role").GetString());
        var me = await _client.SendAsync(Get("/api/auth/me", session.GetProperty("token").GetString()), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("Akosua Mensah", (await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None)).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("+233200000101", "chv-042", "chv")]
    [InlineData("+233200000102", "nurse-17", "nurse")]
    [InlineData("+233200000103", "dr-eshun", "doctor")]
    [InlineData("+233200000104", "ghs-officer-07", "district_officer")]
    [InlineData("+233200000105", "auditor-02", "admin")]
    public async Task Each_of_the_five_demo_workers_signs_in_with_the_demo_code(string phone, string id, string role)
    {
        var session = await SignInAsync(phone, "123456");

        Assert.Equal(id, session.GetProperty("user").GetProperty("id").GetString());
        Assert.Equal(role, session.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("Shai-Osudoku", session.GetProperty("user").GetProperty("district").GetString());
        Assert.Equal(_time.GetUtcNow().AddDays(7), session.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_errors_come_back_in_the_shapes_the_app_reads()
    {
        var unknown = await _client.PostAsJsonAsync("/api/auth/code", new { phone = "+233201111111" }, CancellationToken.None);
        var badPhone = await _client.PostAsJsonAsync("/api/auth/code", new { phone = "12345" }, CancellationToken.None);
        var wrong = await _client.PostAsJsonAsync("/api/auth/verify", new { phone = "+233200000101", code = "000000" }, CancellationToken.None);
        var badCode = await _client.PostAsJsonAsync("/api/auth/verify", new { phone = "+233200000101", code = "abc" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);                 // the app says "not a registered worker"
        Assert.Equal("unknown_number", (await unknown.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badPhone.StatusCode);
        Assert.NotNull((await badPhone.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None)).GetProperty("errors").GetProperty("phone").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);               // the app says "wrong code"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badCode.StatusCode);      // so does this
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_refused_and_does_not_crash_anything()
    {
        var response = await _client.PostAsync("/api/auth/verify", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ the token

    [Fact]
    public async Task Without_a_token_or_with_a_bad_one_the_answer_is_401()
    {
        var token = await TokenAsync("+233200000101");
        var tampered = token[..^3] + (token.EndsWith("AAA", StringComparison.Ordinal) ? "BBB" : "AAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Get("/api/auth/me"), CancellationToken.None)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Get("/api/auth/me", "not.a.token"), CancellationToken.None)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Get("/api/auth/me", tampered), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task A_token_works_for_seven_days_and_not_a_minute_longer()
    {
        var token = await TokenAsync("+233200000101");

        _time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Get("/api/auth/me", token), CancellationToken.None)).StatusCode);

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Get("/api/auth/me", token), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task A_token_still_works_after_the_API_restarts_because_the_key_is_kept_in_the_database()
    {
        var token = await TokenAsync("+233200000101");

        await using var restarted = NewFactory();
        using var client = restarted.CreateClient();
        await ApiFactory.HealthWhenReadyAsync(client);
        await api.WaitForDemoWorkersAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/auth/me", token), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task A_worker_switched_off_after_signing_in_is_signed_out_at_the_next_check()
    {
        var token = await TokenAsync("+233200000102");
        await using var source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using (var off = source.CreateCommand("UPDATE users SET active = false WHERE id = 'nurse-17'"))
        {
            await off.ExecuteNonQueryAsync();
        }

        try
        {
            var me = await _client.SendAsync(Get("/api/auth/me", token), CancellationToken.None);

            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        finally
        {
            await using var on = source.CreateCommand("UPDATE users SET active = true WHERE id = 'nurse-17'");   // the other tests share this worker
            await on.ExecuteNonQueryAsync();
        }
    }

    // ------------------------------------------------------------------ who may do what

    [Theory]
    [InlineData("clinical", "+233200000101", 200)]   // chv
    [InlineData("clinical", "+233200000102", 200)]   // nurse
    [InlineData("clinical", "+233200000103", 200)]   // doctor
    [InlineData("clinical", "+233200000104", 403)]   // district officer: never a patient
    [InlineData("clinical", "+233200000105", 403)]   // auditor: never a patient
    [InlineData("clinician", "+233200000101", 403)]  // a CHV cannot handle referrals
    [InlineData("clinician", "+233200000102", 200)]
    [InlineData("clinician", "+233200000103", 200)]
    [InlineData("clinician", "+233200000104", 403)]
    [InlineData("clinician", "+233200000105", 403)]
    [InlineData("summary", "+233200000101", 403)]
    [InlineData("summary", "+233200000102", 403)]
    [InlineData("summary", "+233200000103", 403)]
    [InlineData("summary", "+233200000104", 200)]    // the officer sees the district summary
    [InlineData("summary", "+233200000105", 200)]    // and so does the auditor
    [InlineData("auditor", "+233200000101", 403)]
    [InlineData("auditor", "+233200000102", 403)]
    [InlineData("auditor", "+233200000103", 403)]
    [InlineData("auditor", "+233200000104", 403)]
    [InlineData("auditor", "+233200000105", 200)]    // only the auditor reads the activity log
    public async Task Each_access_rule_lets_in_exactly_the_roles_the_app_shows_its_pages_to(string rule, string phone, int expected)
    {
        var token = await TokenAsync(phone);

        var response = await _client.SendAsync(Get($"/test/{rule}", token), CancellationToken.None);

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("clinical")]
    [InlineData("clinician")]
    [InlineData("summary")]
    [InlineData("auditor")]
    public async Task Every_rule_needs_a_signed_in_person_first(string rule)
    {
        var response = await _client.SendAsync(Get($"/test/{rule}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);   // 401 (sign in), not 403 (not allowed)
    }

    // ------------------------------------------------------------------ the demo code, and limits

    [Fact]
    public async Task With_demo_mode_off_the_demo_code_is_just_a_wrong_code()
    {
        await using var strict = NewFactory(new Dictionary<string, string> { ["Demo:Enabled"] = "false" });
        using var client = strict.CreateClient();
        await ApiFactory.HealthWhenReadyAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/verify", new { phone = "+233200000101", code = "123456" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task One_address_cannot_hammer_the_sign_in_endpoints()
    {
        await using var limited = NewFactory(new Dictionary<string, string> { ["Auth:RateLimitPerMinute"] = "4" });
        using var client = limited.CreateClient();
        await ApiFactory.HealthWhenReadyAsync(client);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/auth/verify", new { phone = "+233200000101", code = "000000" }, CancellationToken.None)).StatusCode);
        }

        Assert.Equal(4, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", CancellationToken.None)).StatusCode);   // other endpoints are not limited
    }
}

/// <summary>A database outage must never sign people out: 401 makes the app clear the session, so a short outage answers 503.</summary>
[IntegrationTest]
public sealed class SignInWithoutDatabaseTests
{
    [Fact]
    public async Task When_the_signing_key_cannot_be_loaded_a_request_with_a_token_is_a_503_not_a_401()
    {
        await using var factory = new ApiFactory();   // no database at all
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "a.valid-looking.token");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_no_token_is_still_a_plain_401_because_nothing_needed_the_key()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
