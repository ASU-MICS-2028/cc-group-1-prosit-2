using HealthConnect.AuthService.Models;
using HealthConnect.AuthService.Providers.Implementations;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Sms;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthConnect.AuthService.Features;

/// <summary>
/// POST /api/auth/code: sign-in step 1. Texts a 6-digit code to a registered health worker's number.
/// 204 sent · 404 not a registered worker (the app says so) · 422 not a Ghana number · 429 asked too often · 503 the text could not be sent.
/// </summary>
public sealed partial class RequestCode : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/code", Handle)
            .WithName("RequestCode")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceExtension.RateLimitPolicy)
            .Accepts<RequestCodeRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

    public static async Task<IResult> Handle(
        RequestCodeRequest request,
        AppDbContext db,
        ICodeGenerator codes,
        ICodeHasher hasher,
        ISmsSender sms,
        TimeProvider time,
        IOptions<AuthOptions> options,
        ILogger<RequestCode> logger,
        CancellationToken cancellationToken)
    {
        if (!PhoneNumber.TryParse(request.Phone, out var phone))
        {
            return ApiResults.Validation("phone", "Enter a Ghana mobile number.");
        }

        var settings = options.Value;
        var now = time.GetUtcNow();

        // A patient's number gets its account here (PatientAccounts); a worker's number is always the worker's.
        await PatientAccounts.SyncAsync(db, phone, now, cancellationToken);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == phone.E164 && u.Active, cancellationToken);
        if (user is null)
        {
            LogUnknownNumber(logger, phone.Masked);
            return ApiResults.Error(StatusCodes.Status404NotFound, "unknown_number");
        }

        var tooSoon = now - settings.ResendCooldown;
        var lastHour = now.AddHours(-1);
        if (await db.LoginCodes.AnyAsync(c => c.Phone == phone.E164 && c.CreatedAt > tooSoon, cancellationToken)
            || await db.LoginCodes.CountAsync(c => c.Phone == phone.E164 && c.CreatedAt > lastHour, cancellationToken) >= settings.MaxCodesPerHour)
        {
            LogTooOften(logger, user.Id);
            return ApiResults.Error(StatusCodes.Status429TooManyRequests, "too_many_requests");
        }

        var code = codes.NewCode();
        var row = new LoginCode
        {
            Phone = phone.E164,
            CodeHash = await hasher.HashAsync(phone, code, cancellationToken),
            CreatedAt = now,
            ExpiresAt = now + settings.CodeLifetime,
        };
        db.LoginCodes.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var minutes = (int)settings.CodeLifetime.TotalMinutes;
            await sms.SendAsync(phone, $"HealthConnect code: {code}. It works for {minutes} minutes. Do not share it.", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A code nobody received must not count against the person's hourly limit.
            db.LoginCodes.Remove(row);
            await db.SaveChangesAsync(CancellationToken.None);
            LogSendFailed(logger, exception, user.Id);
            return ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "sms_unavailable");
        }

        LogIssued(logger, user.Id);
        return Results.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in code issued for {UserId}")]
    private static partial void LogIssued(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in code requested for a number that is not a registered worker: {Phone}")]
    private static partial void LogUnknownNumber(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in code requested too often for {UserId}")]
    private static partial void LogTooOften(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The sign-in text for {UserId} could not be sent")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string userId);
}
