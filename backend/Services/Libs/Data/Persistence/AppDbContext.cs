using System.Text;
using HealthConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HealthConnect.Data.Persistence;

/// <summary>
/// The PostgreSQL database. Its tables are made by the SQL migrations in Migrations/Sql (EF Core only runs them and keeps
/// the list of what has been applied), because row-level security, triggers, roles and the audit hash chain cannot be
/// written in EF's model. Entities here only read and write tables that already exist, so each one is mapped with
/// ExcludeFromMigrations(): EF never tries to create or change the table.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<LoginCode> LoginCodes => Set<LoginCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("users", table => table.ExcludeFromMigrations());
            user.HasKey(u => u.Id);
        });

        modelBuilder.Entity<LoginCode>(code =>
        {
            code.ToTable("login_codes", table => table.ExcludeFromMigrations());
            code.HasKey(c => c.Id);
            code.Property(c => c.Id).ValueGeneratedOnAdd();
        });

        UseSnakeCaseColumnNames(modelBuilder);
    }

    /// <summary>The database writes names in lower snake_case (code_hash); C# keeps PascalCase (CodeHash).</summary>
    private static void UseSnakeCaseColumnNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    /// <summary>"CodeHash" to "code_hash", "CreatedAt" to "created_at".</summary>
    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
