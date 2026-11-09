using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthConnect.AuthService.Features;

/// <summary>
/// Writes each sign-in, and each refused attempt by a registered worker, into the hash-chained audit log (access_log), so the auditor
/// can see who signed in and when. Numbers that are not registered workers are not logged here (they would let anyone fill the log);
/// they stay a warning in the server's own log. A failure to write never blocks the sign-in itself.
/// </summary>
internal static partial class SignInAudit
{
    public static async Task RecordAsync(AppDbContext db, AppUser user, string outcome, string detail, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO access_log (accessed_by, role, action, outcome, purpose_of_use, entity, entity_id, detail)
                VALUES ({user.Id}, {user.Role}, 'read', {outcome}, 'HOPERAT', 'sign-in', {user.Id}, {detail})
                """,
                cancellationToken);
        }
        catch (Exception problem) when (problem is not OperationCanceledException)
        {
            LogFailed(logger, problem, user.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not write the sign-in of {UserId} to the audit log")]
    private static partial void LogFailed(ILogger logger, Exception problem, string userId);
}
