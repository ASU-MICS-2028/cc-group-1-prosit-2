using Microsoft.EntityFrameworkCore;

namespace HealthConnect.Data.Persistence;

/// <summary>
/// The PostgreSQL database. Its tables are made by the SQL migrations in Migrations/Sql (EF Core only runs them and keeps
/// the list of what has been applied), because row-level security, triggers, roles and the audit hash chain cannot be
/// written in EF's model. Entities that only read and write existing tables are mapped here as they are needed, with
/// ToTable(..., t => t.ExcludeFromMigrations()) so EF never tries to create them.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
