using System.Net;
using System.Text;
using System.Text.Json;
using HealthConnect.SharedLibrary;
using HealthConnect.SharedLibrary.Sms;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.AuthService.Tests;

/// <summary>Sending the sign-in code through Arkesel. A fake server stands in, so no real text is sent and no account is needed.</summary>
[UnitTest]
public sealed class ArkeselSmsSenderTests
{
    private const string Key = "test-key-not-a-real-key";
    private const string Message = "HealthConnect code: 123456. It works for 10 minutes. Do not share it.";

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return answer(request);
        }
    }

    private static PhoneNumber Phone()
    {
        Assert.True(PhoneNumber.TryParse("020 000 0101", out var phone));
        return phone;
    }

    private static IConfiguration Settings(string? key = Key, string? sender = "HealthConnect", string? url = null)
    {
        var values = new Dictionary<string, string?> { ["Sms:Arkesel:ApiKey"] = key, ["Sms:Arkesel:SenderId"] = sender, ["Sms:Arkesel:Url"] = url };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ArkeselSmsSender Sender(FakeServer server, IConfiguration? settings = null) =>
        new(new HttpClient(server), settings ?? Settings(), NullLogger<ArkeselSmsSender>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task The_code_is_posted_to_arkesel_with_the_key_in_a_header_and_the_number_in_international_form()
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, """{"status":"success","data":[{"id":"abc"}]}"""));

        await Sender(server).SendAsync(Phone(), Message, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, server.Request!.Method);
        Assert.Equal("https://sms.arkesel.com/api/v2/sms/send", server.Request.RequestUri!.ToString());
        Assert.Equal(Key, server.Request.Headers.GetValues("api-key").Single());
        Assert.DoesNotContain(Key, server.Request.RequestUri!.ToString());
        var body = JsonDocument.Parse(server.Body!).RootElement;
        Assert.Equal("HealthConnect", body.GetProperty("sender").GetString());
        Assert.Equal(Message, body.GetProperty("message").GetString());
        Assert.Equal(["233200000101"], body.GetProperty("recipients").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact]
    public async Task A_different_address_can_be_set_for_testing()
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, "{}"));

        await Sender(server, Settings(url: "http://localhost:9999/send")).SendAsync(Phone(), Message, CancellationToken.None);

        Assert.Equal("http://localhost:9999/send", server.Request!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("""{"status":"success"}""")]
    [InlineData("""{"code":"ok"}""")]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[]")]
    public async Task Any_ok_answer_that_does_not_say_error_counts_as_sent(string answer)
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, answer));

        await Sender(server).SendAsync(Phone(), Message, CancellationToken.None);

        Assert.Equal(1, server.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":"error","message":"Invalid API key"}""")]
    [InlineData(HttpStatusCode.PaymentRequired, """{"status":"error","message":"Insufficient balance"}""")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.OK, """{"status":"error","message":"Invalid sender"}""")]
    [InlineData(HttpStatusCode.OK, """{"status":"FAILED"}""")]
    public async Task A_refusal_is_an_error_that_never_shows_the_key_the_text_or_the_whole_number(HttpStatusCode status, string answer)
    {
        var server = new FakeServer(_ => Json(status, answer));

        var problem = await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(server).SendAsync(Phone(), Message, CancellationToken.None));

        Assert.DoesNotContain(Key, problem.Message);
        Assert.DoesNotContain("123456", problem.Message);
        Assert.DoesNotContain("0200000101", problem.Message);
        Assert.DoesNotContain("233200000101", problem.Message);
    }

    [Fact]
    public async Task No_network_and_a_timeout_are_errors_not_crashes()
    {
        var down = new FakeServer(_ => throw new HttpRequestException("no route"));
        var slow = new FakeServer(_ => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(down).SendAsync(Phone(), Message, CancellationToken.None));
        await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(slow).SendAsync(Phone(), Message, CancellationToken.None));
    }

    [Fact]
    public async Task When_the_caller_gives_up_the_cancellation_is_passed_on_not_hidden()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var server = new FakeServer(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(server).SendAsync(Phone(), Message, cancelled.Token));
    }

    [Theory]
    [InlineData(null, "HealthConnect")]
    [InlineData("", "HealthConnect")]
    [InlineData(Key, null)]
    [InlineData(Key, "  ")]
    public async Task Switched_on_without_a_key_or_sender_name_nothing_is_sent(string? key, string? sender)
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(server, Settings(key, sender)).SendAsync(Phone(), Message, CancellationToken.None));

        Assert.Equal(0, server.Calls);
    }

    [Theory]
    [InlineData("arkesel", typeof(ArkeselSmsSender))]
    [InlineData("ARKESEL", typeof(ArkeselSmsSender))]
    [InlineData(null, typeof(LogOnlySmsSender))]
    [InlineData("", typeof(LogOnlySmsSender))]
    [InlineData("somethingelse", typeof(LogOnlySmsSender))]
    public void Arkesel_is_used_only_when_the_provider_setting_says_so(string? provider, Type expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sms:Provider"] = provider }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddLogging().AddSharedLibrary();
        using var provider2 = services.BuildServiceProvider();

        Assert.IsType(expected, provider2.GetRequiredService<ISmsSender>());
    }
}
