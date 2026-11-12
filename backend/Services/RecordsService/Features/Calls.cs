using System.Buffers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HealthConnect.Data;
using HealthConnect.RecordsService.Calls;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Patients;
using HealthConnect.RecordsService.Payments;
using HealthConnect.RecordsService.Store;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HealthConnect.RecordsService.Features;

/// <summary>What the app gets to start a call: the one-time ticket, where to connect, and the ICE servers.</summary>
public sealed record CallTicketDto(string Ticket, DateTimeOffset ExpiresAt, string SocketPath, IReadOnlyList<IceServer> IceServers);

/// <summary>
/// POST /api/calls/{appointmentId}/ticket: a pass to join the video call of one teleconsultation (Lab 7 Part C). Only for a
/// teleconsult the caller can see (the database's own access rules decide that, as for every record), and only while it is
/// still booked.
/// </summary>
public sealed class PostCallTicket : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/calls/{appointmentId:guid}/ticket", Handle)
            .WithName("PostCallTicket")
            .WithTags("Calls")
            .RequireAuthorization(AuthPolicies.ClinicalOrPatient)
            .Produces<CallTicketDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        Guid appointmentId,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        CallTickets tickets,
        CallOptions options,
        PaymentSettings payments,
        PaymentStore paid,
        PatientRecords patients,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // A worker who may see the visit, or the patient whose visit it is (they may call in from their own phone).
        var (refusal, appointment, user) = await VisitAccess.FindAsync(principal, sessions, patients, appointmentId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (appointment!.Dto.Type != "teleconsult")
        {
            return ApiResults.Validation("appointmentId", "Only a teleconsultation has a video call.");
        }

        if (appointment.Dto.Status != "scheduled" || appointment.Dto.ReferralStatus == "declined")
        {
            return ApiResults.Validation("appointmentId", "This teleconsultation is closed.");
        }

        // Only around the booked time: from a little before (people need a moment to get ready) until it has had time to run late.
        // An emergency can start as soon as it's booked.
        var (opens, closes) = CallWindow.For(appointment.Dto);
        var now = time.GetUtcNow();
        if (now < opens)
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, "too_early");
        }

        if (now > closes)
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, "too_late");
        }

        // The consultation fee comes first (emergencies never wait); only when payments are switched on for this environment.
        if (payments.Enabled && PaymentRules.MustPay(appointment.Dto) && !await paid.IsPaidAsync(appointmentId, cancellationToken))
        {
            return ApiResults.Error(StatusCodes.Status402PaymentRequired, "payment_required");
        }

        var (ticket, expires) = tickets.Issue(user!, appointmentId);
        return Results.Ok(new CallTicketDto(ticket, expires, $"/api/calls/{appointmentId}/ws", options.ServersFor(user!.Id, expires)));
    }
}

/// <summary>
/// GET /api/calls/{appointmentId}/ws?ticket=...: the signalling relay. The two browsers swap their offer, answer and network
/// candidates through it, then send audio and video to each other directly (or through a TURN relay); the media never
/// passes through this server. The second person to join is told to make the offer.
/// </summary>
public sealed partial class CallSocket : IFeature
{
    public const int MaxMessageBytes = 64 * 1024;

    private static readonly HashSet<string> Relayed = ["offer", "answer", "candidate", "hangup", "media"];

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.Map("/api/calls/{appointmentId:guid}/ws", Handle)
            .WithName("CallSocket")
            .WithTags("Calls")
            .ExcludeFromDescription();

    public static async Task Handle(HttpContext context, Guid appointmentId, CallTickets tickets, CallRooms rooms, ILogger<CallSocket> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (tickets.Redeem(context.Request.Query["ticket"], appointmentId) is not { } ticket)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        var me = new CallParticipant(socket, ticket.User);
        var cancellation = context.RequestAborted;

        var other = rooms.Join(appointmentId, me, out var full);
        if (full)
        {
            await me.SendAsync(Json(new { type = "full" }), cancellation);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "call full", cancellation);
            return;
        }

        LogJoined(logger, ticket.User.Id, ticket.User.Role, appointmentId, rooms.CountIn(appointmentId));
        try
        {
            if (other is null)
            {
                await me.SendAsync(Json(new { type = "waiting" }), cancellation);
            }
            else
            {
                await other.SendAsync(Json(new { type = "peer-joined", peer = Who(ticket.User) }), cancellation);
                await me.SendAsync(Json(new { type = "ready", initiator = true, peer = Who(other.User) }), cancellation);
            }

            await RelayAsync(appointmentId, me, rooms, cancellation);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        {
            // The phone lost its connection: the other side is told below.
        }
        finally
        {
            var left = rooms.Leave(appointmentId, me);
            if (left is not null)
            {
                await left.SendAsync(Json(new { type = "peer-left" }), CancellationToken.None);
            }

            LogLeft(logger, ticket.User.Id, appointmentId);
        }
    }

    private static async Task RelayAsync(Guid appointmentId, CallParticipant me, CallRooms rooms, CancellationToken cancellation)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxMessageBytes);
        try
        {
            while (me.Socket.State == WebSocketState.Open)
            {
                var length = 0;
                ValueWebSocketReceiveResult received;
                do
                {
                    if (length >= MaxMessageBytes)
                    {
                        await me.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", cancellation);
                        return;
                    }

                    received = await me.Socket.ReceiveAsync(buffer.AsMemory(length, MaxMessageBytes - length), cancellation);
                    length += received.Count;
                }
                while (!received.EndOfMessage);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await me.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    return;
                }

                var message = buffer.AsMemory(0, length);
                if (received.MessageType != WebSocketMessageType.Text || !IsRelayed(message.Span))
                {
                    continue;   // pings and anything unknown stop here
                }

                if (rooms.Other(appointmentId, me) is { } other)
                {
                    await other.SendAsync(message.ToArray(), cancellation);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Only the signalling messages a call needs go to the other side, and only well-formed JSON objects.</summary>
    internal static bool IsRelayed(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new Utf8JsonReader(message);
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && Relayed.Contains(type.GetString()!);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object Who(CurrentUser user) => new { name = user.Name, role = user.Role };

    private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserId} ({Role}) joined the call for appointment {AppointmentId}; {Count} in the call")]
    private static partial void LogJoined(ILogger logger, string userId, string role, Guid appointmentId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{UserId} left the call for appointment {AppointmentId}")]
    private static partial void LogLeft(ILogger logger, string userId, Guid appointmentId);
}

/// <summary>
/// When a teleconsult's call can be joined: from <see cref="Before"/> ahead of the booked time until <see cref="After"/> after it.
/// An emergency can start at once. The app uses the same numbers to grey out the Join button.
/// </summary>
public static class CallWindow
{
    public static readonly TimeSpan Before = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan After = TimeSpan.FromHours(2);

    public static (DateTimeOffset Opens, DateTimeOffset Closes) For(AppointmentDto appointment) =>
        (appointment.Priority == "emergency" ? DateTimeOffset.MinValue : appointment.ScheduledAt - Before, appointment.ScheduledAt + After);
}
