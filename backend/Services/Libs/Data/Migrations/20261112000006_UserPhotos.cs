using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HealthConnect.Data.Migrations;

/// <summary>A health worker's profile photo, kept on the server so it is the same on every phone they sign in on.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261112000006_UserPhotos")]
public sealed class UserPhotos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(MigrationSql.Read("0006_user_photos.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations only go forward. Add a new migration to change the database.");
}
