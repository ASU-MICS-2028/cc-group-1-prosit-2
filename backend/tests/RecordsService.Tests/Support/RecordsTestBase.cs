using System.Security.Claims;
using Dapper;
using HealthConnect.Data;
using HealthConnect.Data.Persistence;
using HealthConnect.RecordsService;
using HealthConnect.RecordsService.Events;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;

namespace HealthConnect.RecordsService.Tests.Support;

[CollectionDefinition(Name)]
public sealed class RecordsCollection : ICollectionFixture<RecordsDatabase>
{
    public const string Name = "records";
}

/// <summary>A worker the tests sign in as. They are all invented.</summary>
public sealed record Worker(string Id, string Name, string Role, string District);

/// <summary>A real PostgreSQL with every migration applied and six synthetic workers. Each test starts with no patients.</summary>
public sealed class RecordsDatabase : IAsyncLifetime
{
    public static readonly Worker Chv = new("chv-042", "Akosua Mensah", "chv", "Shai-Osudoku");
    public static readonly Worker OtherChv = new("chv-099", "Kwame Boateng", "chv", "Kintampo North");
    public static readonly Worker Nurse = new("nurse-17", "Efua Asante", "nurse", "Shai-Osudoku");
    public static readonly Worker Doctor = new("dr-eshun", "Kwesi Eshun", "doctor", "Shai-Osudoku");
    public static readonly Worker Officer = new("ghs-officer-07", "Yaw Darko", "district_officer", "Shai-Osudoku");
    public static readonly Worker Admin = new("auditor-02", "Adwoa Boakye", "admin", "Shai-Osudoku");

    private readonly PostgresFixture _postgres = new();

    private Database _database = null!;

    public NpgsqlDataSource Source { get; private set; } = null!;

    /// <summary>One shared connection pool for the whole test run (a pool per test would use up the server's connections).</summary>
    public RoleSessions Sessions { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.ConnectionString).Options);
        await db.Database.MigrateAsync();
        Source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        _database = NewDatabase();
        Sessions = new RoleSessions(_database);
        _ = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddRecordsService();   // sets Dapper column-name rule like the API
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        await Source.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public Database NewDatabase() =>
        new(new DatabaseSettings(_postgres.ConnectionString, null, 5432, null, "x", null, null, null), Substitute.For<IDatabaseSecretReader>());

    public async Task ResetAsync()
    {
        await using var connection = await Source.OpenConnectionAsync();
        await connection.ExecuteAsync("TRUNCATE media, appointments, observations, vitals, consents, patient_identifiers, patients, users RESTART IDENTITY CASCADE");
        var phone = 101;
        foreach (var worker in new[] { Chv, OtherChv, Nurse, Doctor, Officer, Admin })
        {
            await connection.ExecuteAsync(
                "INSERT INTO users (id, phone, name, role, district) VALUES (@Id, @phone, @Name, @Role, @District)",
                new { worker.Id, phone = $"+23320000{phone++:D4}", worker.Name, worker.Role, worker.District });
        }
    }
}

/// <summary>Calls the records endpoints the way the API does: as a signed-in worker, against the real database.</summary>
public abstract class RecordsTestBase(RecordsDatabase database) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    protected RecordsDatabase Database { get; } = database;

    protected FakeTimeProvider Time { get; } = new(Start);

    /// <summary>The appointment events the endpoint published (Lab 7), in order.</summary>
    protected RecordingAppointmentEvents Events { get; } = new();

    protected RoleSessions Sessions => Database.Sessions;

    public Task InitializeAsync() => Database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected static ClaimsPrincipal As(Worker worker) => new(new ClaimsIdentity(
        [new Claim("sub", worker.Id), new Claim("name", worker.Name), new Claim("role", worker.Role), new Claim("district", worker.District)], "test"));

    // ------------------------------------------------------------------ records as the phone makes them

    protected PatientDto Patient(Guid id, string district = "Shai-Osudoku", string given = "Ama", string family = "Owusu", int version = 0) => new(
        Id: id.ToString(),
        GivenName: given,
        FamilyName: family,
        Sex: "female",
        DateOfBirth: new DateOnly(1980, 7, 1),
        BirthDateEstimated: true,
        Phone: null,
        NationalId: null,
        Community: "Dodowa",
        District: district,
        PreferredLanguage: "tw",
        Consent: new ConsentDto(true, "spoken", Start.AddMinutes(-5), "v1", "tw", "chv-042", "Synthetic Witness", null),
        CreatedAt: Start.AddMinutes(-5),
        UpdatedAt: Start.AddMinutes(-5),
        Version: version);

    protected VitalsDto Vitals(Guid id, Guid patientId, int? systolic = 124, int? diastolic = 82, decimal? temperature = 36.8m, int? pulse = 72, decimal? weight = 64.5m, decimal? height = null, string? notes = null) => new(
        Id: id.ToString(),
        PatientId: patientId.ToString(),
        TakenAt: Start.AddMinutes(-2),
        Systolic: systolic,
        Diastolic: diastolic,
        TemperatureC: temperature,
        WeightKg: weight,
        HeightCm: height,
        Pulse: pulse,
        Notes: notes,
        CreatedAt: Start.AddMinutes(-2),
        Version: 0);

    protected AppointmentDto Appointment(Guid id, Guid patientId, string type = "referral", string? referralStatus = null, int version = 0, string status = "scheduled", string priority = "urgent") => new(
        Id: id.ToString(),
        PatientId: patientId.ToString(),
        Type: type,
        ScheduledAt: Start.AddDays(1),
        Facility: "Dodowa District Hospital",
        Reason: "Very high blood pressure",
        Priority: priority,
        Status: status,
        ReferralStatus: referralStatus,
        HandledBy: null,
        HandledAt: null,
        CreatedAt: Start.AddMinutes(-1),
        UpdatedAt: Start.AddMinutes(-1),
        Version: version);

    // ------------------------------------------------------------------ the endpoints

    protected async Task<Reply> PutPatientAsync(Guid id, PatientDto patient, Worker who) =>
        await ResultReader.RunAsync(await PutPatient.Handle(id, patient, As(who), Sessions, Time, NullLogger<PutPatient>.Instance, CancellationToken.None));

    protected async Task<Reply> PutVitalsAsync(Guid id, VitalsDto vitals, Worker who) =>
        await ResultReader.RunAsync(await PutVitals.Handle(id, vitals, As(who), Sessions, Time, NullLogger<PutVitals>.Instance, CancellationToken.None));

    protected async Task<Reply> PutAppointmentAsync(Guid id, AppointmentDto appointment, Worker who) =>
        await ResultReader.RunAsync(await PutAppointment.Handle(id, appointment, As(who), Sessions, Time, NullLogger<PutAppointment>.Instance, Events, new AppointmentEventOptions("arn:test", "test"), CancellationToken.None));

    protected async Task<Reply> PullPatientsAsync(Worker who, DateTimeOffset? since = null) =>
        await ResultReader.RunAsync(await GetPatients.Handle(As(who), Sessions, CancellationToken.None, since));

    protected async Task<Reply> PullVitalsAsync(Worker who, DateTimeOffset? since = null) =>
        await ResultReader.RunAsync(await GetVitals.Handle(As(who), Sessions, CancellationToken.None, since));

    protected async Task<Reply> PullAppointmentsAsync(Worker who, DateTimeOffset? since = null) =>
        await ResultReader.RunAsync(await GetAppointments.Handle(As(who), Sessions, CancellationToken.None, since));

    // ------------------------------------------------------------------ looking at the database directly

    protected async Task<T> ScalarAsync<T>(string sql, object? param = null)
    {
        await using var connection = await Database.Source.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, param) ?? default!;
    }

    protected async Task<List<T>> QueryAsync<T>(string sql, object? param = null)
    {
        await using var connection = await Database.Source.OpenConnectionAsync();
        return [.. await connection.QueryAsync<T>(sql, param)];
    }

    /// <summary>Saves a patient (as the worker who owns the district) and fails the test if it was not created.</summary>
    protected async Task<Guid> SavedPatientAsync(Worker? who = null, string district = "Shai-Osudoku")
    {
        var id = Guid.NewGuid();
        var reply = await PutPatientAsync(id, Patient(id, district), who ?? RecordsDatabase.Chv);
        Assert.Equal(201, reply.Status);
        return id;
    }
}

/// <summary>Keeps the published appointment events instead of sending them to SNS.</summary>
public sealed class RecordingAppointmentEvents : IAppointmentEvents
{
    public List<AppointmentEvent> Published { get; } = [];

    public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken)
    {
        Published.Add(appointmentEvent);
        return Task.CompletedTask;
    }
}
