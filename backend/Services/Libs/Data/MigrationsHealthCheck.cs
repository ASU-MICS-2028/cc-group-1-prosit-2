using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace HealthConnect.Data;

/// <summary>
/// Healthy only when every migration has been applied. This is what stops a release from being called healthy (and
/// kept) while its database changes are missing or failed: the deploy waits for this and rolls back if it never turns green.
/// </summary>
public sealed partial class MigrationsHealthCheck(
    Database database,
    IServiceScopeFactory scopes,
    ILogger<MigrationsHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!database.IsConfigured)
        {
            return HealthCheckResult.Unhealthy("not configured");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(DatabaseHealthCheck.Timeout);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(limit.Token)).Count();
            return pending == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending} migration(s) not applied yet");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller hung up: not a database problem
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception.GetType().Name, exception.Message);
            return HealthCheckResult.Unhealthy();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration check failed: {Reason}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string reason, string detail);
}
