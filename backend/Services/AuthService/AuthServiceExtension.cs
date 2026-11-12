using System.Threading.RateLimiting;
using HealthConnect.AuthService.Features;
using HealthConnect.AuthService.Providers.Implementations;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.Data;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HealthConnect.AuthService;

public static class AuthServiceExtension
{
    /// <summary>Per-address limit on the sign-in endpoints, on top of the per-number limits in the handlers.</summary>
    public const string RateLimitPolicy = "auth";

    public static IServiceCollection AddAuthService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<DemoOptions>(configuration.GetSection(DemoOptions.Section));

        services.AddSingleton<ICodeGenerator, CodeGenerator>();
        services.AddSingleton<ISigningKeyProvider, SigningKeyProvider>();
        services.AddSingleton<ICodeHasher, CodeHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IDatabaseSeeder, DemoUsersSeeder>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ISigningKeyProvider, TimeProvider, IOptions<AuthOptions>>(ConfigureTokenChecking);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.RateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services
            .AddFeature<RequestCode>()
            .AddFeature<VerifyCode>()
            .AddFeature<GetMe>()
            .AddFeature<MyPhoto>();
    }

    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Adds sign-in to the request pipeline: first makes sure the token signing key is loaded, then checks the token, then the
    /// access rules, then the sign-in rate limit. If the key cannot be loaded (the database is down for a moment) the answer is
    /// 503, not 401: a 401 makes the app sign the person out, and a short outage must not sign out every phone in every village.
    /// </summary>
    public static IApplicationBuilder UseAuthService(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await context.RequestServices.GetRequiredService<ISigningKeyProvider>().GetAsync(context.RequestAborted);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("HealthConnect.AuthService.TokenChecking")
                        .LogError(exception, "The token signing key could not be loaded");
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new { error = "service_unavailable" });
                    return;
                }
            }

            await next(context);
        });

        return app.UseAuthentication().UseAuthorization().UseRateLimiter();
    }

    /// <summary>How an incoming token is checked: our signature, issuer, audience and dates, and nothing from a phone is trusted.</summary>
    private static void ConfigureTokenChecking(
        JwtBearerOptions jwt, ISigningKeyProvider keys, TimeProvider time, IOptions<AuthOptions> auth)
    {
        var settings = auth.Value;
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            IssuerSigningKeyResolver = (_, _, _, _) => keys.Cached is { } key ? [key] : [],
            NameClaimType = AuthClaims.Name,
            RoleClaimType = AuthClaims.Role,
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                // Same as the library's own check, but against the injected clock so a test can move time.
                var now = time.GetUtcNow().UtcDateTime;
                return expires is { } end && end >= now - Skew && (notBefore is null || notBefore.Value <= now + Skew);
            },
        };
    }
}
