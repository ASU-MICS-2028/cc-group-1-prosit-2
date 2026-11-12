using HealthConnect.SharedLibrary.Security;
using Npgsql;

namespace HealthConnect.Data;

/// <summary>The four database roles (db/rbac.sql) and which of the app's roles acts as which.</summary>
public static class DbRoles
{
    public const string Chv = "hc_chv";
    public const string Clinician = "hc_clinician";
    public const string Official = "hc_ghs_official";
    public const string Auditor = "hc_auditor";

    /// <summary>The database role for an app role. Anything unknown is refused, never mapped to a default.</summary>
    public static string For(string appRole) => appRole switch
    {
        Roles.Chv => Chv,
        Roles.Nurse or Roles.Doctor => Clinician,
        Roles.DistrictOfficer => Official,
        Roles.Admin => Auditor,
        _ => throw new ArgumentOutOfRangeException(nameof(appRole), appRole, "No database role for this role."),
    };
}

/// <summary>
/// One request's database work: one connection, one transaction, acting as the caller's database role in the caller's district
/// (SET LOCAL ROLE, then the district and user id). From then on Postgres itself refuses what the caller may not see or change
/// (row-level security), so even a bug in the API cannot show a CHV another district's patients. Nothing is saved until
/// <see cref="CommitAsync"/>; disposing without it undoes everything.
/// </summary>
public sealed class RoleSession : IAsyncDisposable
{
    private bool _finished;

    private RoleSession(NpgsqlConnection connection, NpgsqlTransaction transaction, CurrentUser user)
    {
        Connection = connection;
        Transaction = transaction;
        User = user;
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction Transaction { get; }

    public CurrentUser User { get; }

    public static async Task<RoleSession> BeginAsync(NpgsqlDataSource source, CurrentUser user, CancellationToken cancellationToken)
    {
        var role = DbRoles.For(user.Role);   // fixed names from the list above, never text from a request
        var connection = await source.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var switchRole = new NpgsqlCommand($"SET LOCAL ROLE {role}", connection, transaction))
            {
                await switchRole.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var context = new NpgsqlCommand(
                "SELECT set_config('app.district', @district, true), set_config('app.user_id', @user, true)", connection, transaction))
            {
                context.Parameters.AddWithValue("district", user.District);
                context.Parameters.AddWithValue("user", user.Id);
                await context.ExecuteNonQueryAsync(cancellationToken);
            }

            return new RoleSession(connection, transaction, user);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await Transaction.CommitAsync(cancellationToken);
        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finished)
        {
            try
            {
                await Transaction.RollbackAsync();
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
            {
                // the connection is already broken: there is nothing left to undo
            }
        }

        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

/// <summary>
/// Opens a <see cref="RoleSession"/> for the signed-in worker. First it checks, as the API's own login, that the worker still exists
/// and is switched on: a token lives 7 days, and someone switched off must lose access to patient data at once, not at expiry.
/// </summary>
public sealed class RoleSessions(Database database)
{
    /// <summary>The session, or null when the worker is unknown or switched off (the caller answers 401).</summary>
    public async Task<RoleSession?> TryBeginAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        // Still active, and still the role the token says: a worker switched off or given another role on the Health workers page
        // loses access at their next request, not when their token runs out (the app then asks them to sign in again).
        await using (var check = database.DataSource.CreateCommand("SELECT count(*) FROM users WHERE id = @id AND active AND role = @role"))
        {
            check.Parameters.AddWithValue("id", user.Id);
            check.Parameters.AddWithValue("role", user.Role);
            if ((long)(await check.ExecuteScalarAsync(cancellationToken))! == 0)
            {
                return null;
            }
        }

        return await RoleSession.BeginAsync(database.DataSource, user, cancellationToken);
    }
}
