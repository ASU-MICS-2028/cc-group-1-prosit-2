using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>Test results (with photos of lab slips and X-ray films) and prescriptions.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261112000009_ResultsAndPrescriptions")]
public sealed class ResultsAndPrescriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0009_results_and_prescriptions.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
