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

/// <summary>Sending the sign-in code through httpSMS, and switching texts on from the environment's SMS secret. A fake server stands in.</summary>
[UnitTest]
public sealed class HttpSmsSenderTests
{
    private const string Key = "test-key-not-a-real-key";
    private const string From = "+233200000999";
    private const string Passphrase = "phone passphrase";
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

    private static IConfiguration Settings(string? key = Key, string? from = From, string? passphrase = Passphrase) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sms:HttpSms:ApiKey"] = key,
            ["Sms:HttpSms:From"] = from,
            ["Sms:HttpSms:EncryptionKey"] = passphrase,
        }).Build();

    private static HttpSmsSender Sender(FakeServer server, IConfiguration? settings = null) =>
        new(new HttpClient(server), settings ?? Settings(), NullLogger<HttpSmsSender>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task The_code_is_sent_from_the_phone_number_encrypted_so_httpsms_cannot_read_it()
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, """{"status":"success","data":{"status":"pending"}}"""));

        await Sender(server).SendAsync(Phone(), Message, CancellationToken.None);

        Assert.Equal("https://api.httpsms.com/v1/messages/send", server.Request!.RequestUri!.ToString());
        Assert.Equal(Key, server.Request.Headers.GetValues("x-api-key").Single());
        var body = JsonDocument.Parse(server.Body!).RootElement;
        Assert.Equal(From, body.GetProperty("from").GetString());
        Assert.Equal("+233200000101", body.GetProperty("to").GetString());
        Assert.True(body.GetProperty("encrypted").GetBoolean());
        var content = body.GetProperty("content").GetString()!;
        Assert.DoesNotContain("123456", content);
        Assert.Equal(Message, HttpSmsSender.Decrypt(Passphrase, content));
    }

    [Fact]
    public async Task Without_a_passphrase_the_text_goes_as_it_is()
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, """{"status":"success"}"""));

        await Sender(server, Settings(passphrase: null)).SendAsync(Phone(), Message, CancellationToken.None);

        var body = JsonDocument.Parse(server.Body!).RootElement;
        Assert.False(body.GetProperty("encrypted").GetBoolean());
        Assert.Equal(Message, body.GetProperty("content").GetString());
    }

    [Fact]
    public void Encryption_matches_the_reminder_function_and_the_httpsms_format_byte_for_byte()
    {
        // Same passphrase, text and IV through functions/src/aes_cfb.py (checked there against the NIST test vectors).
        const string fromPython =
            "AAECAwQFBgcICQoLDA0OD14+PUOYRezpgNLj313iU/tzAJ+/BPOqEvVDFM4jw//bI5+jt0NlMgUKWrcFS8UG7PrGtYGzwrJ6CuNu4+WdnQpmrEM/fw==";
        byte[] iv = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

        Assert.Equal(fromPython, HttpSmsSender.Encrypt(Passphrase, Message, iv));
        Assert.Equal(Message, HttpSmsSender.Decrypt(Passphrase, fromPython));
    }

    [Fact]
    public void Every_message_gets_a_fresh_iv()
    {
        Assert.NotEqual(HttpSmsSender.Encrypt(Passphrase, Message), HttpSmsSender.Encrypt(Passphrase, Message));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":"error","message":"invalid api key"}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"status":"error","message":"phone not found"}""")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.OK, """{"status":"error"}""")]
    public async Task A_refusal_is_an_error_that_never_shows_the_key_the_text_or_the_whole_number(HttpStatusCode status, string answer)
    {
        var server = new FakeServer(_ => Json(status, answer));

        var problem = await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(server).SendAsync(Phone(), Message, CancellationToken.None));

        Assert.DoesNotContain(Key, problem.Message);
        Assert.DoesNotContain("123456", problem.Message);
        Assert.DoesNotContain("200000101", problem.Message);
    }

    [Fact]
    public async Task An_unreachable_service_is_an_sms_error()
    {
        var server = new FakeServer(_ => throw new HttpRequestException("no route"));

        await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(server).SendAsync(Phone(), Message, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, From)]
    [InlineData(Key, null)]
    [InlineData(Key, " ")]
    public async Task Switched_on_without_a_key_or_from_number_nothing_is_sent(string? key, string? from)
    {
        var server = new FakeServer(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<SmsDeliveryException>(() => Sender(server, Settings(key, from)).SendAsync(Phone(), Message, CancellationToken.None));

        Assert.Equal(0, server.Calls);
    }

    // ---- switching texts on from the environment's secret ----

    private static ISmsSender SenderFromSecret(string? secretId, string? secretJson)
    {
        var configuration = new ConfigurationBuilder().AddSmsSecret(secretId, _ => secretJson).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddLogging().AddSharedLibrary();
        return services.BuildServiceProvider().GetRequiredService<ISmsSender>();
    }

    [Fact]
    public void An_httpsms_secret_switches_httpsms_on()
    {
        var sender = SenderFromSecret("healthconnect/staging/sms", $$"""{"provider":"httpsms","apiKey":"{{Key}}","from":"{{From}}","encryptionKey":"x"}""");

        Assert.IsType<HttpSmsSender>(sender);
    }

    [Fact]
    public void An_arkesel_secret_switches_arkesel_on_with_a_default_sender_name()
    {
        var settings = SmsSecret.ToSettings("""{"provider":"Arkesel","apiKey":"k"}""");

        Assert.Equal("arkesel", settings["Sms:Provider"]);
        Assert.Equal("k", settings["Sms:Arkesel:ApiKey"]);
        Assert.Equal("HlthConnect", settings["Sms:Arkesel:SenderId"]);
        Assert.IsType<ArkeselSmsSender>(SenderFromSecret("id", """{"provider":"arkesel","apiKey":"k"}"""));
    }

    [Theory]
    [InlineData(null, null)]          // no SMS_SECRET_ID: a laptop, the tests, the OpenAPI build
    [InlineData("healthconnect/staging/sms", null)] // the secret does not exist yet
    [InlineData("id", """{"provider":"log"}""")]
    public void Without_a_secret_texts_are_only_logged(string? secretId, string? secretJson)
    {
        Assert.IsType<LogOnlySmsSender>(SenderFromSecret(secretId, secretJson));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"provider":"carrier-pigeon","apiKey":"secret-value"}""")]
    public void A_malformed_secret_stops_start_up_without_showing_it(string secretJson)
    {
        var problem = Assert.Throws<InvalidOperationException>(() => SmsSecret.ToSettings(secretJson));

        Assert.DoesNotContain("secret-value", problem.Message);
    }
}
