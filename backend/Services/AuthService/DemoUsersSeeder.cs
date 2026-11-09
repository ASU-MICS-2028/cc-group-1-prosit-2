using HealthConnect.Data;
using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthConnect.AuthService;

/// <summary>
/// Adds the five synthetic demo health workers (the same ones the app's offline demo mode has, with the same ids) when Demo:Enabled
/// is on. It only adds what is missing and never changes a worker who already exists, so it is safe on every start.
/// A number already belonging to someone else is left alone.
/// </summary>
public sealed partial class DemoUsersSeeder(IOptions<DemoOptions> options, TimeProvider time, ILogger<DemoUsersSeeder> logger) : IDatabaseSeeder
{
    public const string District = "Shai-Osudoku";

    /// <summary>Invented people on placeholder numbers (020 000 010x). Nothing here is real.</summary>
    public static readonly IReadOnlyList<(string Id, string Phone, string Name, string Role)> Workers =
    [
        ("chv-042", "+233200000101", "Akosua Mensah", Roles.Chv),
        ("nurse-17", "+233200000102", "Efua Asante", Roles.Nurse),
        ("dr-eshun", "+233200000103", "Kwesi Eshun", Roles.Doctor),
        ("ghs-officer-07", "+233200000104", "Yaw Darko", Roles.DistrictOfficer),
        ("auditor-02", "+233200000105", "Adwoa Boakye", Roles.Admin),
    ];

    public async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        LogEnabled(logger);
        var now = time.GetUtcNow();
        foreach (var (id, phone, name, role) in Workers)
        {
            if (await db.Users.AnyAsync(u => u.Id == id || u.Phone == phone, cancellationToken))
            {
                continue;
            }

            db.Users.Add(new AppUser { Id = id, Phone = phone, Name = name, Role = role, District = District, Demo = true, CreatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo workers are on: the fixed demo code signs in the five synthetic accounts. Set Demo:Enabled to false before real people use the system.")]
    private static partial void LogEnabled(ILogger logger);
}
