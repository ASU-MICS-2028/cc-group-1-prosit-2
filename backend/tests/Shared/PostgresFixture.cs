using Testcontainers.PostgreSql;
using Xunit;

namespace HealthConnect.Tests.Shared;

/// <summary>
/// One real PostgreSQL (in Docker) for a test class or a whole test project (share it with a collection fixture).
/// Same major version as production (RDS PostgreSQL 17).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
