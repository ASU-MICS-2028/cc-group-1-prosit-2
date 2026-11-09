using HealthConnect.Data.Persistence;

namespace HealthConnect.Data;

/// <summary>
/// Adds data a database should start with (the demo health workers). Runs right after the migrations, every time the API starts,
/// so it must be safe to run again: add what is missing, never overwrite.
/// </summary>
public interface IDatabaseSeeder
{
    Task SeedAsync(AppDbContext db, CancellationToken cancellationToken);
}
