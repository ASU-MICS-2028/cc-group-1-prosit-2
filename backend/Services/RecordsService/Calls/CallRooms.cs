using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using HealthConnect.SharedLibrary.Security;
using Microsoft.Extensions.Configuration;

namespace HealthConnect.RecordsService.Calls;

/// <summary>
/// A one-time pass to join one teleconsult's call. A browser cannot put the sign-in token in a WebSocket's headers, and a
/// long-lived token in a web address would end up in logs, so the app swaps its token for this: random, single use, valid
/// for one minute and for one appointment only.
/// </summary>
public sealed record CallTicket(CurrentUser User, Guid AppointmentId, DateTimeOffset ExpiresAt);

public sealed class CallTickets(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, CallTicket> _tickets = new(StringComparer.Ordinal);

    public (string Token, DateTimeOffset ExpiresAt) Issue(CurrentUser user, Guid appointmentId)
    {
        var now = time.GetUtcNow();
        foreach (var (key, old) in _tickets)
        {
            if (old.ExpiresAt <= now)
            {
                _tickets.TryRemove(key, out _);
            }
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ticket = new CallTicket(user, appointmentId, now.Add(Lifetime));
        _tickets[token] = ticket;
        return (token, ticket.ExpiresAt);
    }

    /// <summary>The ticket, used up; null when it is unknown, used, expired, or for another call.</summary>
    public CallTicket? Redeem(string? token, Guid appointmentId)
    {
        if (string.IsNullOrEmpty(token) || !_tickets.TryRemove(token, out var ticket))
        {
            return null;
        }

        return ticket.AppointmentId == appointmentId && ticket.ExpiresAt > time.GetUtcNow() ? ticket : null;
    }
}

/// <summary>A STUN or TURN server the browsers use to find a path to each other (RTCIceServer).</summary>
public sealed record IceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);

/// <summary>
/// The ICE servers handed to the app. STUN (free, public) finds each phone's public address; it is enough when at least one
/// side's NAT allows a direct path. Mobile networks in Ghana usually sit behind carrier-grade NAT, and then only a TURN relay
/// works. Two ways to configure one:
///   TURN_URLS + TURN_SECRET: our coturn relay (deploy/terraform/turn.tf) with the TURN REST scheme. Every ticket gets its own
///     login, "&lt;expiry&gt;:&lt;user&gt;" with an HMAC-SHA1 of it as the password, valid for <see cref="TurnLoginLifetime"/>. The
///     relay checks it with the same secret, so nothing is stored and a login copied from a browser stops working.
///   TURN_URLS + TURN_USERNAME + TURN_CREDENTIAL: a fixed login (a hosted relay such as Metered or Twilio).
/// </summary>
public sealed record CallOptions(IReadOnlyList<IceServer> IceServers, IReadOnlyList<string>? TurnUrls = null, string? TurnSecret = null)
{
    public const string DefaultStun = "stun:stun.l.google.com:19302,stun:stun1.l.google.com:19302";

    /// <summary>Long enough for a consultation that starts late and runs over; a relay login is only checked when a call starts.</summary>
    public static readonly TimeSpan TurnLoginLifetime = TimeSpan.FromHours(4);

    public static CallOptions From(IConfiguration configuration)
    {
        static string[] List(string? value) => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var servers = new List<IceServer> { new(List(configuration["CALL_STUN_URLS"] ?? DefaultStun)) };
        var turn = List(configuration["TURN_URLS"]);
        var secret = configuration["TURN_SECRET"];
        if (turn.Length > 0 && !string.IsNullOrWhiteSpace(secret))
        {
            return new CallOptions(servers, turn, secret);
        }

        if (turn.Length > 0)
        {
            servers.Add(new IceServer(turn, configuration["TURN_USERNAME"], configuration["TURN_CREDENTIAL"]));
        }

        return new CallOptions(servers);
    }

    /// <summary>The servers for one ticket: the fixed ones, plus a relay login of its own when the relay uses a shared secret.</summary>
    public IReadOnlyList<IceServer> ServersFor(string userId, DateTimeOffset now)
    {
        if (TurnUrls is not { Count: > 0 } urls || string.IsNullOrWhiteSpace(TurnSecret))
        {
            return IceServers;
        }

        var username = $"{(now + TurnLoginLifetime).ToUnixTimeSeconds()}:{userId}";
        var password = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(TurnSecret), Encoding.UTF8.GetBytes(username)));
        return [.. IceServers, new IceServer(urls, username, password)];
    }
}

/// <summary>One side of a call, with a lock so two relays never write to its socket at once.</summary>
public sealed class CallParticipant(WebSocket socket, CurrentUser user)
{
    private readonly SemaphoreSlim _send = new(1, 1);

    public WebSocket Socket { get; } = socket;

    public CurrentUser User { get; } = user;

    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        await _send.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            _send.Release();
        }
    }
}

/// <summary>
/// Who is in which call, in this server's memory. A call has at most two people (the CHV with the patient, and the doctor).
/// Kept in memory because there is one API server; with more than one, the rooms would move to a shared store (Redis, or
/// API Gateway WebSockets), since both sides of a call must reach the same relay.
/// </summary>
public sealed class CallRooms
{
    public const int MaxParticipants = 2;

    private readonly ConcurrentDictionary<Guid, List<CallParticipant>> _rooms = new();

    /// <summary>Joins the call; returns who was already there, or null when it is full.</summary>
    public CallParticipant? Join(Guid appointmentId, CallParticipant participant, out bool full)
    {
        var room = _rooms.GetOrAdd(appointmentId, _ => []);
        lock (room)
        {
            if (room.Count >= MaxParticipants)
            {
                full = true;
                return null;
            }

            full = false;
            var other = room.FirstOrDefault();
            room.Add(participant);
            return other;
        }
    }

    /// <summary>The other person in the call, if any.</summary>
    public CallParticipant? Other(Guid appointmentId, CallParticipant participant)
    {
        if (!_rooms.TryGetValue(appointmentId, out var room))
        {
            return null;
        }

        lock (room)
        {
            return room.FirstOrDefault(p => !ReferenceEquals(p, participant));
        }
    }

    public CallParticipant? Leave(Guid appointmentId, CallParticipant participant)
    {
        if (!_rooms.TryGetValue(appointmentId, out var room))
        {
            return null;
        }

        lock (room)
        {
            room.Remove(participant);
            if (room.Count == 0)
            {
                _rooms.TryRemove(new KeyValuePair<Guid, List<CallParticipant>>(appointmentId, room));
            }

            return room.FirstOrDefault();
        }
    }

    public int CountIn(Guid appointmentId) => _rooms.TryGetValue(appointmentId, out var room) ? room.Count : 0;
}
