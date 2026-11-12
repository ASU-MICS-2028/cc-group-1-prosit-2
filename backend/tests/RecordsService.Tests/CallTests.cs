using System.Text;
using HealthConnect.RecordsService.Calls;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Tests.Support;
using HealthConnect.SharedLibrary.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace HealthConnect.RecordsService.Tests;

/// <summary>POST /api/calls/{id}/ticket: only for a teleconsult the caller can see, while it is still booked.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class CallTicketEndpointTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private readonly CallTickets _tickets = new(TimeProvider.System);
    private readonly CallOptions _options = new([new IceServer(["stun:example.org:3478"])]);

    private async Task<Reply> TicketAsync(Guid appointment, Worker who) =>
        await ResultReader.RunAsync(await PostCallTicket.Handle(appointment, As(who), Sessions, _tickets, _options, PaymentsOff, Payments, CancellationToken.None));

    private async Task<Guid> BookedAsync(string type, string status = "scheduled")
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        Assert.Equal(201, (await PutAppointmentAsync(id, Appointment(id, patient, type, status: status), RecordsDatabase.Chv)).Status);
        return id;
    }

    [Fact]
    public async Task A_teleconsult_gets_a_ticket_with_the_ice_servers()
    {
        var call = await BookedAsync("teleconsult");

        var reply = await TicketAsync(call, RecordsDatabase.Doctor);

        Assert.Equal(200, reply.Status);
        Assert.Equal($"/api/calls/{call}/ws", reply.Body.GetProperty("socketPath").GetString());
        Assert.Equal("stun:example.org:3478", reply.Body.GetProperty("iceServers")[0].GetProperty("urls")[0].GetString());
        Assert.NotNull(_tickets.Redeem(reply.Body.GetProperty("ticket").GetString(), call));
    }

    [Fact]
    public async Task An_ordinary_visit_has_no_call()
    {
        var visit = await BookedAsync("follow_up");
        Assert.Equal(422, (await TicketAsync(visit, RecordsDatabase.Chv)).Status);
    }

    [Fact]
    public async Task A_closed_teleconsult_has_no_call()
    {
        var call = await BookedAsync("teleconsult", status: "cancelled");
        Assert.Equal(422, (await TicketAsync(call, RecordsDatabase.Chv)).Status);
    }

    [Fact]
    public async Task An_unknown_or_hidden_appointment_is_not_found()
    {
        Assert.Equal(404, (await TicketAsync(Guid.NewGuid(), RecordsDatabase.Chv)).Status);
        var call = await BookedAsync("teleconsult");
        Assert.Equal(404, (await TicketAsync(call, RecordsDatabase.OtherChv)).Status);   // another district's CHV
    }

    [Fact]
    public async Task Someone_who_does_not_work_with_patients_cannot_get_one()
    {
        var call = await BookedAsync("teleconsult");
        var reply = await TicketAsync(call, RecordsDatabase.Officer);
        Assert.NotEqual(200, reply.Status);
    }
}

public sealed class CallTicketTests
{
    private static readonly CurrentUser Chv = new("chv-042", "Akosua Mensah", "chv", "Shai-Osudoku");

    [Fact]
    public void A_ticket_is_single_use_for_one_call_and_expires_after_a_minute()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tickets = new CallTickets(time);
        var call = Guid.NewGuid();

        var (token, _) = tickets.Issue(Chv, call);
        Assert.Equal(Chv, tickets.Redeem(token, call)!.User);
        Assert.Null(tickets.Redeem(token, call));

        var (other, _) = tickets.Issue(Chv, call);
        Assert.Null(tickets.Redeem(other, Guid.NewGuid()));

        var (late, _) = tickets.Issue(Chv, call);
        time.Advance(CallTickets.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Null(tickets.Redeem(late, call));
        Assert.Null(tickets.Redeem(null, call));
    }

    [Theory]
    [InlineData("""{"type":"offer","sdp":{}}""", true)]
    [InlineData("""{"type":"candidate","candidate":null}""", true)]
    [InlineData("""{"type":"hangup"}""", true)]
    [InlineData("""{"type":"ping"}""", false)]
    [InlineData("""{"type":"join","as":"admin"}""", false)]
    [InlineData("""["offer"]""", false)]
    [InlineData("not json", false)]
    public void Only_signalling_messages_are_relayed(string message, bool relayed) =>
        Assert.Equal(relayed, CallSocket.IsRelayed(Encoding.UTF8.GetBytes(message)));

    [Fact]
    public void Stun_by_default_and_turn_only_when_configured()
    {
        var none = CallOptions.From(new ConfigurationBuilder().Build());
        var server = Assert.Single(none.IceServers);
        Assert.Contains("stun:stun.l.google.com:19302", server.Urls);

        var withTurn = CallOptions.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TURN_URLS"] = "turn:turn.example.org:3478?transport=udp, turns:turn.example.org:443",
            ["TURN_USERNAME"] = "hc",
            ["TURN_CREDENTIAL"] = "secret",
        }).Build());
        Assert.Equal(2, withTurn.IceServers.Count);
        Assert.Equal(["turn:turn.example.org:3478?transport=udp", "turns:turn.example.org:443"], withTurn.IceServers[1].Urls);
        Assert.Equal("hc", withTurn.IceServers[1].Username);
    }

    [Fact]
    public void A_shared_secret_relay_gets_a_login_of_its_own_per_ticket_that_the_relay_can_check()
    {
        var options = CallOptions.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TURN_URLS"] = "turn:198.51.100.7:3478?transport=udp,turn:198.51.100.7:3478?transport=tcp",
            ["TURN_SECRET"] = "shared-secret",
        }).Build());
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

        var servers = options.ServersFor("chv-042", now);

        Assert.Equal(2, servers.Count);
        var relay = servers[1];
        var expiry = now + CallOptions.TurnLoginLifetime;
        Assert.Equal($"{expiry.ToUnixTimeSeconds()}:chv-042", relay.Username);
        // coturn's use-auth-secret: password = base64(HMAC-SHA1(secret, username))
        var expected = Convert.ToBase64String(System.Security.Cryptography.HMACSHA1.HashData(
            Encoding.UTF8.GetBytes("shared-secret"), Encoding.UTF8.GetBytes(relay.Username!)));
        Assert.Equal(expected, relay.Credential);
        Assert.NotEqual(relay.Username, options.ServersFor("dr-eshun", now)[1].Username);
        Assert.DoesNotContain(options.IceServers, s => s.Credential is not null); // the secret itself is never handed out
    }
}
