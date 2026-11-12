using System.Security.Claims;
using System.Text.Json;
using HealthConnect.Data;
using HealthConnect.RecordsService.Models;
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

/// <summary>
/// Which visits have a fee. A teleconsult costs <see cref="PaymentSettings.FeePesewas"/>; an emergency never waits for money, and
/// other visit types are free at the point of care.
/// </summary>
public static class PaymentRules
{
    public static bool HasFee(AppointmentDto a) => a.Type == "teleconsult";

    public static bool Exempt(AppointmentDto a) => HasFee(a) && a.Priority == "emergency";

    public static bool MustPay(AppointmentDto a) => HasFee(a) && !Exempt(a);
}

/// <summary>Where a visit's fee stands, for the app. status: not_required, exempt, unpaid, pending, paid, failed or abandoned.</summary>
public sealed record PaymentStatusDto(
    Guid AppointmentId,
    bool Enabled,
    string Status,
    int AmountPesewas,
    string Currency,
    string? Reference,
    string? Channel,
    DateTimeOffset? PaidAt);

public sealed record CheckoutRequest(string? CallbackUrl);

public sealed record CheckoutDto(string Reference, string AuthorizationUrl, int AmountPesewas, string Currency);

public sealed record PaymentListItemDto(
    string Reference,
    Guid AppointmentId,
    string District,
    int AmountPesewas,
    string Status,
    string? Channel,
    string CollectedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string Facility,
    DateTimeOffset ScheduledAt);

public sealed record PaymentListDto(string? District, int PaidCount, int PaidPesewas, int PendingCount, List<PaymentListItemDto> Items);

internal static class PaymentViews
{
    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static PaymentStatusDto Status(Guid id, PaymentSettings settings, AppointmentDto appointment, PaymentRow? row)
    {
        if (!PaymentRules.HasFee(appointment))
        {
            return new(id, settings.Enabled, "not_required", 0, "GHS", null, null, null);
        }

        if (row is { Status: "paid" })
        {
            return new(id, settings.Enabled, "paid", row.AmountPesewas, row.Currency, row.Reference, row.Channel, row.PaidAt is { } at ? Utc(at) : null);
        }

        if (PaymentRules.Exempt(appointment))
        {
            return new(id, settings.Enabled, "exempt", 0, "GHS", null, null, null);
        }

        return row is null
            ? new(id, settings.Enabled, "unpaid", settings.FeePesewas, "GHS", null, null, null)
            : new(id, settings.Enabled, row.Status, row.AmountPesewas, row.Currency, row.Reference, row.Channel, null);
    }

    /// <summary>The visit, if this worker may see it: the role session's row-level security decides, exactly as for the visit itself.</summary>
    public static async Task<(IResult? Refusal, AppointmentDto? Appointment)> VisibleAsync(
        ClaimsPrincipal principal, RoleSessions sessions, Guid appointmentId, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } user)
        {
            return (ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token"), null);
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        if (session is null)
        {
            return (ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user"), null);
        }

        StoredAppointment? stored;
        try
        {
            stored = await AppointmentStore.FindAsync(session, appointmentId, cancellationToken);
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return (ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden"), null);
        }

        return stored is null ? (ApiResults.Error(StatusCodes.Status404NotFound, "not_found"), null) : (null, stored.Dto);
    }
}

/// <summary>
/// GET /api/payments/{appointmentId}: the visit's fee and whether it is paid. A payment still pending is checked with Paystack on the
/// spot, so the page the phone returns to after paying shows the result without waiting for a webhook.
/// </summary>
public sealed class GetPayment : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/payments/{appointmentId:guid}", Handle)
            .WithName("GetPayment")
            .WithTags("Payments")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<PaymentStatusDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    public static async Task<IResult> Handle(
        Guid appointmentId,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        PaymentStore payments,
        PaymentSettings settings,
        IPaystack paystack,
        ILogger<GetPayment> logger,
        CancellationToken cancellationToken)
    {
        var (refusal, appointment) = await PaymentViews.VisibleAsync(principal, sessions, appointmentId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var row = await payments.LatestAsync(appointmentId, cancellationToken);
        if (row is { Status: "pending" } && settings.Enabled)
        {
            try
            {
                if (await paystack.CheckAsync(row.Reference, cancellationToken) is { } transaction)
                {
                    row = await payments.ApplyAsync(transaction, cancellationToken) ?? row;
                }
            }
            catch (Exception problem) when (problem is PaystackException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning("Could not check payment {Reference} with Paystack ({Problem}); it stays pending", row.Reference, problem.GetType().Name);
            }
        }

        return Results.Ok(PaymentViews.Status(appointmentId, settings, appointment!, row));
    }
}

/// <summary>
/// POST /api/payments/{appointmentId}/checkout { callbackUrl }: starts a mobile-money payment of the teleconsult fee and returns
/// Paystack's checkout page for the phone to open. Paystack sends the person back to callbackUrl, which must be this app's
/// /appointments/{id}/payment page. 409 already paid · 422 no fee, exempt or closed · 503 payments not switched on · 502 Paystack down.
/// </summary>
public sealed class PostCheckout : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/payments/{appointmentId:guid}/checkout", Handle)
            .WithName("PostPaymentCheckout")
            .WithTags("Payments")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<CheckoutDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

    /// <summary>Paystack only ever sends people back to our own payment page for this visit (https, or http on a developer's machine).</summary>
    public static bool IsAllowedCallback(string? url, Guid appointmentId) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && uri.AbsolutePath == $"/appointments/{appointmentId}/payment"
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);

    public static async Task<IResult> Handle(
        Guid appointmentId,
        CheckoutRequest request,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        PaymentStore payments,
        PaymentSettings settings,
        IPaystack paystack,
        CancellationToken cancellationToken)
    {
        var (refusal, appointment) = await PaymentViews.VisibleAsync(principal, sessions, appointmentId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!settings.Enabled)
        {
            return ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "payments_off");
        }

        if (!PaymentRules.HasFee(appointment!))
        {
            return ApiResults.Validation("appointmentId", "Only a teleconsultation has a fee.");
        }

        if (PaymentRules.Exempt(appointment!))
        {
            return ApiResults.Validation("appointmentId", "An emergency has no fee.");
        }

        if (appointment!.Status != "scheduled" || appointment.ReferralStatus == "declined")
        {
            return ApiResults.Validation("appointmentId", "This teleconsultation is closed.");
        }

        if (!IsAllowedCallback(request.CallbackUrl, appointmentId))
        {
            return ApiResults.Validation("callbackUrl", "Must be this app's payment page for the visit.");
        }

        if (await payments.IsPaidAsync(appointmentId, cancellationToken))
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, "already_paid");
        }

        var user = CurrentUser.From(principal)!;
        var reference = $"hc-{Guid.NewGuid():N}";
        var row = await payments.StartAsync(reference, appointmentId, settings.FeePesewas, user.Id, cancellationToken);
        try
        {
            // Paystack needs an e-mail address; patients rarely have one, so each visit gets a placeholder that can't receive mail.
            var checkout = await paystack.StartAsync(
                reference, row.AmountPesewas, $"visit-{appointmentId:N}@example.com", request.CallbackUrl!, appointmentId, cancellationToken);
            return Results.Ok(new CheckoutDto(reference, checkout.AuthorizationUrl, row.AmountPesewas, row.Currency));
        }
        catch (Exception problem) when (problem is PaystackException or HttpRequestException or TaskCanceledException)
        {
            await payments.MarkAsync(reference, "failed", CancellationToken.None);
            return ApiResults.Error(StatusCodes.Status502BadGateway, "payment_provider_unavailable");
        }
    }
}

/// <summary>
/// POST /api/payments/paystack/webhook: Paystack telling us a charge succeeded. Only accepted with a valid x-paystack-signature, and even
/// then the amount and currency must match what we asked for. Unknown references are acknowledged and ignored.
/// </summary>
public sealed class PaystackWebhook : IFeature
{
    public const int MaxBytes = 64 * 1024;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/payments/paystack/webhook", Handle)
            .WithName("PaystackWebhook")
            .WithTags("Payments")
            .AllowAnonymous()
            .ExcludeFromDescription();

    public static async Task<IResult> Handle(
        HttpRequest request, PaymentStore payments, PaymentSettings settings, ILogger<PaystackWebhook> logger, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Results.NotFound();
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        var body = buffer.ToArray();
        if (!settings.IsSignedByPaystack(body, request.Headers["x-paystack-signature"]))
        {
            logger.LogWarning("Refused a Paystack webhook with a missing or wrong signature");
            return Results.Unauthorized();
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.GetProperty("event").GetString() == "charge.success")
            {
                var transaction = PaystackClient.FromJson(json.RootElement.GetProperty("data"));
                var row = await payments.ApplyAsync(transaction, cancellationToken);
                logger.LogInformation("Paystack webhook for {Reference}: {Status}", transaction.Reference, row?.Status ?? "unknown reference");
            }
        }
        catch (Exception problem) when (problem is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning("A signed Paystack webhook could not be read ({Problem})", problem.GetType().Name);
        }

        return Results.Ok();
    }
}

/// <summary>
/// GET /api/payments: fees collected, newest first: the district officer sees their district, the auditor every district. Visits are
/// described by facility and time only, never by patient.
/// </summary>
public sealed class GetPayments : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/payments", Handle)
            .WithName("GetPayments")
            .WithTags("Payments")
            .RequireAuthorization(AuthPolicies.Summary)
            .Produces<PaymentListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

    public static async Task<IResult> Handle(ClaimsPrincipal principal, PaymentStore payments, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var district = user.Role == Roles.Admin ? null : user.District;
        var rows = await payments.ListAsync(district, cancellationToken);
        var items = rows.Select(r => new PaymentListItemDto(
            r.Reference, r.AppointmentId, r.District, r.AmountPesewas, r.Status, r.Channel, r.CreatedByName ?? r.CreatedBy,
            PaymentViews.Utc(r.CreatedAt), r.PaidAt is { } at ? PaymentViews.Utc(at) : null, r.Facility, PaymentViews.Utc(r.ScheduledAt))).ToList();
        var paid = items.Where(i => i.Status == "paid").ToList();
        return Results.Ok(new PaymentListDto(district, paid.Count, paid.Sum(i => i.AmountPesewas), items.Count(i => i.Status == "pending"), items));
    }
}
