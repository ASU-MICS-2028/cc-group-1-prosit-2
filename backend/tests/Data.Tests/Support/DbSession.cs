using Npgsql;

namespace HealthConnect.Data.Tests.Support;

/// <summary>The four database roles the API acts as (db/rbac.sql).</summary>
public static class Roles
{
    public const string Chv = "hc_chv";
    public const string Clinician = "hc_clinician";
    public const string Official = "hc_ghs_official";
    public const string Auditor = "hc_auditor";
}

/// <summary>
/// One database connection inside one transaction, rolled back when the test ends. It starts as the database owner (who may do
/// anything), and <see cref="AsAsync"/> switches into a role the way the API does for every request:
/// SET LOCAL ROLE, then the person's district.
/// </summary>
public sealed class DbSession : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private int _savepoints;

    private DbSession(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public static async Task<DbSession> StartAsync(NpgsqlDataSource source)
    {
        var connection = await source.OpenConnectionAsync();
        return new DbSession(connection, await connection.BeginTransactionAsync());
    }

    /// <summary>Act as this role, with the district the API would set for the signed-in person.</summary>
    public async Task AsAsync(string role, string? district = null, string? userId = null)
    {
        await ExecAsync($"SET LOCAL ROLE {role}");
        await ExecAsync("SELECT set_config('app.district', @d, true), set_config('app.user_id', @u, true)",
            ("d", district ?? string.Empty), ("u", userId ?? string.Empty));
    }

    /// <summary>Back to the database owner.</summary>
    public Task AsOwnerAsync() => ExecAsync("RESET ROLE");

    public async Task<int> ExecAsync(string sql, params (string Name, object? Value)[] args)
    {
        await using var command = Command(sql, args);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] args)
    {
        await using var command = Command(sql, args);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)value;
    }

    public async Task<long> CountAsync(string sql, params (string Name, object? Value)[] args) =>
        await ScalarAsync<long>(sql, args);

    public async Task<List<object?[]>> RowsAsync(string sql, params (string Name, object? Value)[] args)
    {
        await using var command = Command(sql, args);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Runs a statement that the database must refuse, and returns the refusal (SqlState, message, constraint).
    /// The failed statement is undone on its own, so the rest of the test can carry on in the same transaction.
    /// </summary>
    public async Task<PostgresException> FailsAsync(string sql, params (string Name, object? Value)[] args)
    {
        var savepoint = $"s{++_savepoints}";
        await ExecAsync($"SAVEPOINT {savepoint}");
        try
        {
            await ExecAsync(sql, args);
        }
        catch (PostgresException refusal)
        {
            await ExecAsync($"ROLLBACK TO SAVEPOINT {savepoint}");
            return refusal;
        }

        throw new Xunit.Sdk.XunitException($"The database accepted a statement it should have refused: {sql}");
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private NpgsqlCommand Command(string sql, (string Name, object? Value)[] args)
    {
        var command = new NpgsqlCommand(sql, _connection, _transaction);
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

/// <summary>PostgreSQL error codes the tests expect.</summary>
public static class SqlState
{
    public const string CheckViolation = "23514";
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string InsufficientPrivilege = "42501";   // also "violates row-level security policy"
    public const string RaisedByTrigger = "P0001";
}
