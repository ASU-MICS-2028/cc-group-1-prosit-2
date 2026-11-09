using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HealthConnect.Data.Tests.Support;

[CollectionDefinition(Name)]
public sealed class SchemaCollection : ICollectionFixture<SchemaFixture>
{
    /// <summary>Every test that uses the migrated database is in this collection, so they run one after another.</summary>
    public const string Name = "schema";
}

/// <summary>
/// A real PostgreSQL 17 (Docker) with every migration applied, the way the API applies them at start-up.
/// Tests open a <see cref="DbSession"/> on it: one transaction that is rolled back at the end, so tests never see each other's rows.
/// </summary>
public sealed class SchemaFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public NpgsqlDataSource Admin { get; private set; } = null!;

    public string ConnectionString => _postgres.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await MigrateAsync(ConnectionString);
        Admin = NpgsqlDataSource.Create(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await Admin.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Applies every pending migration to the database in the connection string.</summary>
    public static async Task MigrateAsync(string connectionString)
    {
        await using var db = NewContext(connectionString);
        await db.Database.MigrateAsync();
    }

    public static AppDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>Makes an empty database in the same server and returns the connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using var command = Admin.CreateCommand($"CREATE DATABASE {name}");
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public Task<DbSession> StartAsync() => DbSession.StartAsync(Admin);
}
