using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthConnect.Data;

/// <summary>
/// Brings the database up to date when the API starts. It runs in the background and retries, because the database may
/// not answer yet (a restart, a slow network). Migrations run in one transaction each and EF holds a lock while they run,
/// so two API containers starting together cannot both apply the same migration.
/// </summary>
public sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopes,
    Database database,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(Math.Max(options.Value.MigrationRetrySeconds, 0.05));

        if (!options.Value.MigrateOnStartup)
        {
            LogSkipped(logger, "switched off (Database:MigrateOnStartup)");
            return;
        }

        if (!database.IsConfigured)
        {
            LogSkipped(logger, "no database is configured");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync(stoppingToken)).Count();
                await db.Database.MigrateAsync(stoppingToken);
                LogReady(logger, pending);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogRetry(logger, exception, retryDelay.TotalSeconds);
                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations not run: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date ({Applied} migration(s) applied now)")]
    private static partial void LogReady(ILogger logger, int applied);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not bring the database up to date, retrying in {Seconds} s")]
    private static partial void LogRetry(ILogger logger, Exception exception, double seconds);
}
