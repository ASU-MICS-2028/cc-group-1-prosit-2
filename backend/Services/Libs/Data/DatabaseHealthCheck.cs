using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HealthConnect.Data;

/// <summary>
/// Healthy only if the API can really reach the database right now: open a connection and answer "select 1".
/// It gives up after a few seconds, and says nothing about why (no host names or messages go to the caller;
/// the reason is in the log).
/// </summary>
public sealed partial class DatabaseHealthCheck(Database database, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!database.IsConfigured)
        {
            return HealthCheckResult.Unhealthy("not configured");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Timeout);
        try
        {
            await using var connection = await database.DataSource.OpenConnectionAsync(limit.Token);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(limit.Token);
            return HealthCheckResult.Healthy();
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database health check failed: {Reason}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string reason, string detail);
}
