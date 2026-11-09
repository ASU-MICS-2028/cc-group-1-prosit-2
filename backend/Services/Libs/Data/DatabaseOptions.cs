namespace HealthConnect.Data;

/// <summary>Settings under "Database".</summary>
public sealed class DatabaseOptions
{
    /// <summary>
    /// Bring the database up to date when the API starts (retrying until the database answers). On by default, so the
    /// server needs no extra setting: a deploy that cannot migrate never turns healthy and is rolled back.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>How long to wait before trying again when the database cannot be migrated yet (it may not answer, or a migration failed).</summary>
    public double MigrationRetrySeconds { get; set; } = 5;
}
