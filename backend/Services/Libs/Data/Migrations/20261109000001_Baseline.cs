using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>Liza's schema and roles (db/schema.sql and db/rbac.sql from Lab 6). Safe on a database that already has them.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261109000001_Baseline")]
public sealed class Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0001_baseline.sql"));

    // Migrations only go forward: a health database is never rolled back by dropping tables. Fix forward with a new migration.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
