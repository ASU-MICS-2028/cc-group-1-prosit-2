using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HealthConnect.RecordsService.Calls;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.Api.Tests;

/// <summary>Lab 7 Part C: the call's signalling relay, end to end over real WebSockets (no database needed).</summary>
public sealed class CallSocketTests : IAsyncLifetime
{
    private static readonly CurrentUser Chv = new("chv-042", "Akosua Mensah", "chv", "Shai-Osudoku");
    private static readonly CurrentUser Doctor = new("dr-eshun", "Kwesi Eshun", "doctor", "Shai-Osudoku");

    private readonly ApiFactory _factory = new(migrate: false);
    private readonly Guid _call = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private string Ticket(CurrentUser user, Guid? call = null) =>
        _factory.Services.GetRequiredService<CallTickets>().Issue(user, call ?? _call).Token;

    private async Task<WebSocket> JoinAsync(string ticket)
    {
        var client = _factory.Server.CreateWebSocketClient();
        return await client.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"api/calls/{_call}/ws?ticket={ticket}"), CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement.Clone();
    }

    private static Task SendAsync(WebSocket socket, object message) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task Two_people_join_the_second_makes_the_offer_and_the_relay_passes_it_on()
    {
        using var chv = await JoinAsync(Ticket(Chv));
        Assert.Equal("waiting", (await ReceiveAsync(chv)).GetProperty("type").GetString());

        using var doctor = await JoinAsync(Ticket(Doctor));
        var joined = await ReceiveAsync(chv);
        Assert.Equal("peer-joined", joined.GetProperty("type").GetString());
        Assert.Equal("doctor", joined.GetProperty("peer").GetProperty("role").GetString());
        var ready = await ReceiveAsync(doctor);
        Assert.Equal("ready", ready.GetProperty("type").GetString());
        Assert.True(ready.GetProperty("initiator").GetBoolean());

        await SendAsync(doctor, new { type = "ping" });   // not relayed
        await SendAsync(doctor, new { type = "offer", sdp = new { type = "offer", sdp = "v=0" } });
        var offer = await ReceiveAsync(chv);
        Assert.Equal("offer", offer.GetProperty("type").GetString());
        Assert.Equal("v=0", offer.GetProperty("sdp").GetProperty("sdp").GetString());

        await SendAsync(chv, new { type = "answer", sdp = new { type = "answer", sdp = "v=0 answer" } });
        Assert.Equal("answer", (await ReceiveAsync(doctor)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_third_person_is_turned_away()
    {
        using var chv = await JoinAsync(Ticket(Chv));
        await ReceiveAsync(chv);
        using var doctor = await JoinAsync(Ticket(Doctor));
        await ReceiveAsync(doctor);

        using var third = await JoinAsync(Ticket(Doctor));

        Assert.Equal("full", (await ReceiveAsync(third)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task When_one_side_hangs_up_the_other_is_told()
    {
        using var chv = await JoinAsync(Ticket(Chv));
        await ReceiveAsync(chv);
        var doctor = await JoinAsync(Ticket(Doctor));
        await ReceiveAsync(chv);

        await doctor.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

        Assert.Equal("peer-left", (await ReceiveAsync(chv)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_ticket_works_once_and_only_for_its_own_call()
    {
        var ticket = Ticket(Chv);
        using var first = await JoinAsync(ticket);

        await Assert.ThrowsAnyAsync<Exception>(() => JoinAsync(ticket));                       // used
        await Assert.ThrowsAnyAsync<Exception>(() => JoinAsync(Ticket(Chv, Guid.NewGuid())));  // another call
        await Assert.ThrowsAnyAsync<Exception>(() => JoinAsync("made-up"));
    }
}
