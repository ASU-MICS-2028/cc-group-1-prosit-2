using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>The audit chain follows the order entries are committed, so many writers at once cannot make it look tampered with.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261109000005_AuditChainOrder")]
public sealed class AuditChainOrder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0005_audit_chain_order.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
