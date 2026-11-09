using System.Security.Cryptography;
using System.Text;
using HealthConnect.AuthService.Models;
using HealthConnect.AuthService.Providers.Implementations;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthConnect.AuthService.Features;

/// <summary>
/// POST /api/auth/verify: sign-in step 2. Checks the latest code for the number and, if it matches, signs the person in.
/// 200 { token, expiresAt, user } · 401 wrong or expired code · 422 not a number or not 6 digits · 429 too many wrong tries.
/// A code works once, expires after 10 minutes and allows 5 wrong guesses. The answer never says whether a number is registered:
/// an unknown number and a wrong code both give 401.
/// </summary>
public sealed partial class VerifyCode : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/verify", Handle)
            .WithName("VerifyCode")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceExtension.RateLimitPolicy)
            .Accepts<VerifyCodeRequest>("application/json")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    public static async Task<IResult> Handle(
        VerifyCodeRequest request,
        AppDbContext db,
        ICodeHasher hasher,
        ITokenIssuer tokens,
        TimeProvider time,
        IOptions<AuthOptions> options,
        IOptions<DemoOptions> demo,
        ILogger<VerifyCode> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        if (!PhoneNumber.TryParse(request.Phone, out var phone))
        {
            errors["phone"] = "Enter a Ghana mobile number.";
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length != CodeGenerator.Length || !code.All(char.IsAsciiDigit))
        {
            errors["code"] = "Enter the 6-digit code.";
        }

        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        var settings = options.Value;
        var now = time.GetUtcNow();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == phone.E164 && u.Active, cancellationToken);
        if (user is null)
        {
            LogUnknownNumber(logger, phone.Masked);
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "wrong_code");
        }

        // The fixed demo code: only for the synthetic demo workers, and only while Demo:Enabled is on.
        if (demo.Value.Enabled && user.Demo && FixedTimeEquals(code, demo.Value.Code))
        {
            LogDemoSignIn(logger, user.Id, user.Role);
            return await SignInAsync(user, tokens, cancellationToken);
        }

        var login = await db.LoginCodes
            .Where(c => c.Phone == phone.E164 && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (login is null || login.ExpiresAt <= now)
        {
            LogRefused(logger, user.Id, "no valid code");
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "code_expired");
        }

        if (login.Attempts >= settings.MaxAttempts)
        {
            LogRefused(logger, user.Id, "too many wrong tries");
            return ApiResults.Error(StatusCodes.Status429TooManyRequests, "too_many_attempts");
        }

        if (!await hasher.MatchesAsync(phone, code, login.CodeHash, cancellationToken))
        {
            // One atomic step, so two wrong guesses at once both count.
            await db.LoginCodes.Where(c => c.Id == login.Id).ExecuteUpdateAsync(
                set => set.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken);
            LogRefused(logger, user.Id, "wrong code");
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "wrong_code");
        }

        // A code works once: only the request that marks it used signs anyone in, even if two arrive together.
        var claimed = await db.LoginCodes.Where(c => c.Id == login.Id && c.UsedAt == null).ExecuteUpdateAsync(
            set => set.SetProperty(c => c.UsedAt, now), cancellationToken);
        if (claimed == 0)
        {
            LogRefused(logger, user.Id, "code already used");
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "wrong_code");
        }

        LogSignedIn(logger, user.Id, user.Role);
        return await SignInAsync(user, tokens, cancellationToken);
    }

    private static async Task<IResult> SignInAsync(AppUser user, ITokenIssuer tokens, CancellationToken cancellationToken)
    {
        var token = await tokens.IssueAsync(user, cancellationToken);
        return Results.Ok(new AuthResponse(token.Value, token.ExpiresAt, new UserDto(user.Id, user.Name, user.Role, user.District)));
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    [LoggerMessage(Level = LogLevel.Information, Message = "Signed in: {UserId} ({Role})")]
    private static partial void LogSignedIn(ILogger logger, string userId, string role);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signed in with the demo code: {UserId} ({Role}). Switch Demo:Enabled off before real use.")]
    private static partial void LogDemoSignIn(ILogger logger, string userId, string role);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused for {UserId}: {Reason}")]
    private static partial void LogRefused(ILogger logger, string userId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in tried with a number that is not a registered worker: {Phone}")]
    private static partial void LogUnknownNumber(ILogger logger, string phone);
}
