using System.Security.Claims;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Store;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace HealthConnect.RecordsService.Patients;

/// <summary>
/// What a signed-in patient may read: the records registered with their account's phone number, and nothing else. Read with the API's
/// own login (the workers' database roles and their district rules don't apply to a patient), so the phone number taken from the
/// users table by the token's id is the one and only filter.
/// </summary>
public sealed class PatientRecords(Database database)
{
    /// <summary>The account's phone number, or null when it is not an active patient account.</summary>
    public async Task<string?> PhoneAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT phone FROM users WHERE id = @userId AND role = 'patient' AND active", new { userId }, cancellationToken: cancellationToken));
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        await database.DataSource.OpenConnectionAsync(cancellationToken);
}

/// <summary>
/// One visit as the caller may see it: a worker through their role session (row-level security decides), a patient only when the visit
/// belongs to a record registered with their number. Anyone else, or a visit they may not see, is told it doesn't exist.
/// </summary>
internal static class VisitAccess
{
    public static async Task<(IResult? Refusal, StoredAppointment? Visit, CurrentUser? User)> FindAsync(
        ClaimsPrincipal principal, RoleSessions sessions, PatientRecords patients, Guid appointmentId, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } user)
        {
            return (ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token"), null, null);
        }

        if (user.Role == Roles.Patient)
        {
            if (await patients.PhoneAsync(user.Id, cancellationToken) is not { } phone)
            {
                return (ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user"), null, user);
            }

            await using var connection = await patients.OpenAsync(cancellationToken);
            var mine = await AppointmentStore.FindForPhoneAsync(connection, appointmentId, phone, cancellationToken);
            return mine is null ? (ApiResults.Error(StatusCodes.Status404NotFound, "not_found"), null, user) : (null, mine, user);
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        if (session is null)
        {
            return (ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user"), null, user);
        }

        StoredAppointment? visit;
        try
        {
            visit = await AppointmentStore.FindAsync(session, appointmentId, cancellationToken);
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return (ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden"), null, user);
        }

        return visit is null ? (ApiResults.Error(StatusCodes.Status404NotFound, "not_found"), null, user) : (null, visit, user);
    }
}
