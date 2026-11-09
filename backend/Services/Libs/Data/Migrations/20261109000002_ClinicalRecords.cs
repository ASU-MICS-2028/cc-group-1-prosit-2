using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>The tables and columns the app needs beyond the first draft: vitals, appointments, users, login codes, media, consent proof.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261109000002_ClinicalRecords")]
public sealed class ClinicalRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0002_clinical_records.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
