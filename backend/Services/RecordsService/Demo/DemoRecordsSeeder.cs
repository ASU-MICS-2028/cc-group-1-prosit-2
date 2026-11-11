using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HealthConnect.Data;
using HealthConnect.Data.Persistence;
using HealthConnect.RecordsService.Events;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Models;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.RecordsService.Demo;

/// <summary>
/// Fills a demo database with invented patients, readings, visits and referrals, so every screen has something real to show:
/// the CHV's caseload and graphs, the nurse's referral inbox, and the district officer's weekly counts (some weeks big enough to
/// be shown, one district too small to be shown). Everyone here is made up; names end in "-Demo".
///
/// It goes through the same code the API uses (so every rule is checked, and the audit and chain rules apply), as the demo
/// CHV and the demo nurse. It runs after the migrations on every start, only while Demo:Enabled and Demo:SampleData are on, and
/// it is safe to repeat: every record has a fixed id, so what exists is left alone, and each new week adds that week's readings,
/// so the graphs stay current. Nothing is overwritten.
/// </summary>
public sealed partial class DemoRecordsSeeder(
    IConfiguration configuration, RoleSessions sessions, TimeProvider time, ILogger<DemoRecordsSeeder> logger) : IDatabaseSeeder
{
    public const string SettingName = "Demo:SampleData";
    public const int Weeks = 8;

    // Invented patients must not get reminder texts or page a clinician: the demo publishes no appointment events.
    private static readonly IAppointmentEvents NoEvents = new NoAppointmentEvents();
    private static readonly AppointmentEventOptions NoEventOptions = new(null, "demo");

    private static readonly Seedling Chv = new("chv-042", "Akosua Mensah", "chv", "Shai-Osudoku");
    private static readonly Seedling Nurse = new("nurse-17", "Efua Asante", "nurse", "Shai-Osudoku");

    private sealed record Seedling(string Id, string Name, string Role, string District)
    {
        public ClaimsPrincipal Principal => new(new ClaimsIdentity(
            [new Claim("sub", Id), new Claim("name", Name), new Claim("role", Role), new Claim("district", District)], "demo-seed"));
    }

    /// <summary>A district, who lives there, and for each of the last 8 weeks (oldest first) how many were seen, how many had high blood pressure, how many a fever.</summary>
    private sealed record Place(
        string District, string[] Communities, string Language, string[] People, int[] Seen, int[] HighBp, int[] Fever, Seedling Creator);

    private static readonly Place[] Places =
    [
        new("Shai-Osudoku", ["Dodowa", "Doryumu", "Ayikuma", "Asutsuare", "Kasunya"], "tw",
            ["Ama Owusu", "Kofi Mensah", "Abena Quaye", "Yaw Tetteh", "Efua Adjei", "Kwame Ofori", "Akua Boateng", "Kojo Annan", "Adwoa Lamptey", "Nii Armah", "Maame Sarpong", "Selorm Dzorgbenu"],
            [8, 9, 8, 10, 9, 9, 10, 6], [2, 2, 3, 2, 3, 3, 3, 2], [0, 1, 0, 1, 1, 0, 1, 1], Chv),
        new("Kintampo North", ["Kintampo", "Jema", "Babator", "Kunsu"], "tw",
            ["Fuseini Alhassan", "Amina Mohammed", "Issah Abdul", "Salamatu Yakubu", "Kwabena Asare", "Esi Nyarko", "Ibrahim Seidu", "Hawa Iddrisu", "Joseph Danso", "Grace Appiah"],
            [5, 6, 5, 6, 7, 8, 10, 6], [1, 1, 1, 1, 2, 2, 2, 1], [0, 0, 1, 1, 2, 4, 6, 4], Nurse),
        new("Tamale Metropolitan", ["Choggu", "Kalpohin", "Jisonayili"], "dag",
            ["Abdulai Zakaria", "Memunatu Salifu", "Yakubu Alhassan", "Rashida Mumuni", "Sulemana Abu", "Zainab Fuseini", "Haruna Issaka", "Mariama Sulemana"],
            [5, 6, 5, 6, 5, 7, 6, 5], [1, 2, 1, 2, 1, 2, 2, 1], [0, 1, 0, 1, 0, 1, 1, 0], Nurse),
        new("Sawla-Tuna-Kalba", ["Sawla", "Tuna"], "ha",
            ["Kamara Bukari", "Fati Dramani", "Wumbei Nabila"],
            [3, 3, 3, 3, 3, 3, 3, 3], [1, 0, 1, 0, 1, 0, 0, 0], [0, 1, 0, 0, 1, 0, 0, 0], Nurse),
    ];

    public async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await SeedCoreAsync(cancellationToken);
        }
        catch (Exception problem) when (problem is not OperationCanceledException)
        {
            // Sample data is a convenience: a problem here must never stop the API from starting or the other seeders from running.
            LogFailed(logger, problem);
        }
    }

    private async Task SeedCoreAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Demo:Enabled") || !configuration.GetValue("Demo:SampleData", true))
        {
            return;
        }

        // The demo workers must exist and be switched on (the demo-workers seeder runs first).
        foreach (var worker in new[] { Chv, Nurse })
        {
            await using var session = await sessions.TryBeginAsync(new CurrentUser(worker.Id, worker.Name, worker.Role, worker.District), cancellationToken);
            if (session is null)
            {
                LogSkipped(logger, worker.Id);
                return;
            }
        }

        var now = time.GetUtcNow();
        var saved = 0;
        var patientIds = new Dictionary<(string District, int Person), Guid>();
        foreach (var place in Places)
        {
            for (var person = 0; person < place.People.Length; person++)
            {
                var id = StableId($"patient:{place.District}:{person}");
                patientIds[(place.District, person)] = id;
                var reply = await Run(PutPatient.Handle(id, Patient(place, person, id, now), place.Creator.Principal, sessions, time, NullLogger<PutPatient>.Instance, cancellationToken));
                saved += reply == StatusCodes.Status201Created ? 1 : 0;
            }
        }

        var monday = StartOfWeek(now);
        foreach (var place in Places)
        {
            for (var week = 0; week < Weeks; week++)
            {
                var weekStart = monday.AddDays(-7 * (Weeks - 1 - week));
                saved += await SeedWeekAsync(place, week, weekStart, now, patientIds, cancellationToken);
            }
        }

        saved += await SeedVisitsAsync(patientIds, now, cancellationToken);
        LogSeeded(logger, saved);
    }

    private async Task<int> SeedWeekAsync(
        Place place, int week, DateTimeOffset weekStart, DateTimeOffset now, Dictionary<(string District, int Person), Guid> patientIds, CancellationToken cancellationToken)
    {
        var seen = Math.Min(place.Seen[week], place.People.Length);
        var saved = 0;
        for (var n = 0; n < seen; n++)
        {
            var person = (n + week * 3) % place.People.Length;   // a different mix of people each week
            var patient = patientIds[(place.District, person)];
            var key = $"vitals:{place.District}:{person}:{weekStart:yyyy-MM-dd}";
            var id = StableId(key);
            var takenAt = weekStart.AddDays(n % 5).AddHours(8 + n % 8);
            if (takenAt > now.AddMinutes(-1))
            {
                takenAt = now.AddMinutes(-(n + 1));
            }

            var high = n < place.HighBp[week];
            var fever = n >= seen - place.Fever[week];
            var bits = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            var reading = new VitalsDto(
                Id: id.ToString(),
                PatientId: patient.ToString(),
                TakenAt: takenAt,
                Systolic: high ? 148 + bits[0] % 32 : 108 + bits[0] % 26,
                Diastolic: high ? 92 + bits[1] % 18 : 66 + bits[1] % 18,
                TemperatureC: fever ? 38.1m + bits[2] % 14 / 10m : 36.3m + bits[2] % 9 / 10m,
                WeightKg: 48m + bits[3] % 45,
                HeightCm: null,
                Pulse: 62 + bits[4] % 38,
                Notes: null,
                CreatedAt: takenAt,
                Version: 0);
            var reply = await Run(PutVitals.Handle(id, reading, place.Creator.Principal, sessions, time, NullLogger<PutVitals>.Instance, cancellationToken));
            saved += reply == StatusCodes.Status201Created ? 1 : 0;
        }

        return saved;
    }

    /// <summary>Visits and referrals around the demo CHV's Shai-Osudoku patients, so the visit lists and the nurse's inbox have content.</summary>
    private async Task<int> SeedVisitsAsync(Dictionary<(string District, int Person), Guid> patientIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var saved = 0;
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 10, 0, 0, TimeSpan.Zero);
        Guid Patient(int person) => patientIds[("Shai-Osudoku", person)];

        // (key, person, type, days from today, priority, reason, status, what the nurse does about it)
        var visits = new (string Key, int Person, string Type, int Days, string Priority, string Reason, string Status, string[] Nurse)[]
        {
            ("referral-bp", 0, "referral", 1, "urgent", "Very high blood pressure. BP 172/108 mmHg", "scheduled", []),
            ("referral-fever", 3, "referral", 2, "urgent", "Fever for three days. Temperature 38.9 C", "scheduled", ["accepted"]),
            ("referral-wound", 5, "referral", -3, "routine", "Wound on the leg is not healing", "scheduled", ["accepted", "seen"]),
            ("referral-declined", 7, "referral", 4, "routine", "Cough for two weeks", "scheduled", ["declined"]),
            ("followup-1", 1, "follow_up", 1, "routine", "Blood pressure check", "scheduled", []),
            ("followup-2", 2, "follow_up", 3, "routine", "Antenatal visit", "scheduled", []),
            ("followup-3", 4, "follow_up", 6, "routine", "Review after treatment", "scheduled", []),
            ("overdue-1", 6, "follow_up", -2, "routine", "Blood pressure check", "scheduled", []),
            ("done-1", 8, "follow_up", -5, "routine", "Home visit", "completed", []),
            ("missed-1", 9, "follow_up", -4, "routine", "Home visit", "missed", []),
        };

        foreach (var (key, person, type, days, priority, reason, status, nurse) in visits)
        {
            var id = StableId($"appointment:{key}");
            var appointment = new AppointmentDto(
                Id: id.ToString(),
                PatientId: Patient(person).ToString(),
                Type: type,
                ScheduledAt: today.AddDays(days),
                Facility: "Dodowa District Hospital",
                Reason: reason,
                Priority: priority,
                Status: status,
                ReferralStatus: null,
                HandledBy: null,
                HandledAt: null,
                CreatedAt: today.AddDays(Math.Min(days, 0) - 1),
                UpdatedAt: today.AddDays(Math.Min(days, 0) - 1),
                Version: 0);
            var created = await Run(PutAppointment.Handle(id, appointment, Chv.Principal, sessions, time, NullLogger<PutAppointment>.Instance, NoEvents, NoEventOptions, cancellationToken));
            if (created != StatusCodes.Status201Created)
            {
                continue;   // seeded before: leave it as the nurse and CHV have it now
            }

            saved++;
            var version = 1;
            foreach (var answer in nurse)
            {
                await Run(PutAppointment.Handle(
                    id, appointment with { ReferralStatus = answer, Version = version++ }, Nurse.Principal, sessions, time,
                    NullLogger<PutAppointment>.Instance, NoEvents, NoEventOptions, cancellationToken));
            }
        }

        return saved;
    }

    private static PatientDto Patient(Place place, int person, Guid id, DateTimeOffset now)
    {
        var name = place.People[person].Split(' ');
        var bits = SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString()));
        var born = new DateOnly(1948 + bits[0] % 66, 1 + bits[1] % 12, 1 + bits[2] % 28);
        var created = new DateTimeOffset(now.Year, now.Month, now.Day, 8, 0, 0, TimeSpan.Zero).AddDays(-60);
        return new PatientDto(
            Id: id.ToString(),
            GivenName: name[0],
            FamilyName: $"{name[1]}-Demo",
            Sex: person % 2 == 0 ? "female" : "male",
            DateOfBirth: born,
            BirthDateEstimated: bits[3] % 2 == 0,
            Phone: null,
            NationalId: null,
            Community: place.Communities[person % place.Communities.Length],
            District: place.District,
            PreferredLanguage: place.Language,
            Consent: new ConsentDto(true, "spoken", created, "v1", place.Language, place.Creator.Id, "Community elder (demo)", null),
            CreatedAt: created,
            UpdatedAt: created,
            Version: 0);
    }

    /// <summary>The same text always gives the same id, which is what makes seeding safe to repeat.</summary>
    private static Guid StableId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"healthconnect-demo:{key}"))[..16]);

    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    private static async Task<int> Run(Task<IResult> call) => (await call) is IStatusCodeHttpResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo sample data: {Saved} new records")]
    private static partial void LogSeeded(ILogger logger, int saved);

    [LoggerMessage(Level = LogLevel.Error, Message = "Demo sample data could not be completed; it will be tried again at the next start")]
    private static partial void LogFailed(ILogger logger, Exception problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo sample data skipped: {WorkerId} is not available")]
    private static partial void LogSkipped(ILogger logger, string workerId);
}
