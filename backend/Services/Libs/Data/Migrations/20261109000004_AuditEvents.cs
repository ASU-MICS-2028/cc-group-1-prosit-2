using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>The audit log learns the references the app's own audit events carry, so uploads can be repeated safely.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261109000004_AuditEvents")]
public sealed class AuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0004_audit_events.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
