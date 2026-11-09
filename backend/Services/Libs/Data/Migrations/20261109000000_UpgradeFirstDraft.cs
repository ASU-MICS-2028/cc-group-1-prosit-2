using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>
/// If the database was made from Liza's first draft of the schema (Lab 6 starter), bring it up to the shape the baseline expects.
/// Does nothing on a new database or one that already has the extended schema. Runs before the baseline.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261109000000_UpgradeFirstDraft")]
public sealed class UpgradeFirstDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0000_upgrade_first_draft.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
