using HealthConnect.Data.Tests.Support;

namespace HealthConnect.Data.Tests;

/// <summary>
/// surveillance_weekly: the only thing a district officer may read. Counts per district and week, never names, and any
/// cell under 5 patients is hidden so nobody in a small village can be picked out (Act 843 s.24(3)).
/// </summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class SurveillanceViewTests(SchemaFixture fixture)
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static async Task<List<object?[]>> SummaryAsync(DbSession db, string? district = null) =>
        await db.RowsAsync(
            "SELECT district, week::text, patients_seen, patients_high_bp, patients_fever, suppressed FROM surveillance_weekly " +
            "WHERE (@d::text IS NULL OR district = @d) ORDER BY district, week",
            ("d", district));

    [Fact]
    public async Task Four_patients_are_hidden()
    {
        await using var db = await fixture.StartAsync();
        for (var i = 0; i < 4; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Kintampo, Monday);
        }

        var row = (await SummaryAsync(db, Seed.Kintampo)).Single();

        Assert.Null(row[2]);
        Assert.Null(row[3]);
        Assert.Null(row[4]);
        Assert.Equal(true, row[5]);
    }

    [Fact]
    public async Task Five_patients_are_shown()
    {
        await using var db = await fixture.StartAsync();
        for (var i = 0; i < 5; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Kintampo, Monday);
        }

        var row = (await SummaryAsync(db, Seed.Kintampo)).Single();

        Assert.Equal("2026-11-02", row[1]);
        Assert.Equal(5L, row[2]);
        Assert.Equal(false, row[5]);
    }

    [Fact]
    public async Task One_patient_with_many_readings_counts_once()
    {
        await using var db = await fixture.StartAsync();
        for (var i = 0; i < 4; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Kintampo, Monday);
        }

        var fifth = await db.AddPatientWithReadingAsync(Seed.Kintampo, Monday);
        for (var i = 0; i < 6; i++)
        {
            await db.AddReadingAsync(fifth, "heart_rate", "80", Monday.AddHours(i));
        }

        var row = (await SummaryAsync(db, Seed.Kintampo)).Single();

        Assert.Equal(5L, row[2]);
    }

    [Fact]
    public async Task High_blood_pressure_and_fever_are_counted_by_patient()
    {
        await using var db = await fixture.StartAsync();
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, systolic: "152", diastolic: "96");   // high BP
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, systolic: "120", diastolic: "92");   // high diastolic only
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, systolic: "139", diastolic: "89");   // just under both limits
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, temperature: "38.0");                // fever (the limit counts)
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, temperature: "38.6");                // fever
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, temperature: "37.9");                // just under

        var row = (await SummaryAsync(db, Seed.Shai)).Single();

        Assert.Equal(6L, row[2]);
        Assert.Equal(2L, row[3]);
        Assert.Equal(2L, row[4]);
    }

    [Fact]
    public async Task A_reading_marked_entered_in_error_is_left_out()
    {
        await using var db = await fixture.StartAsync();
        for (var i = 0; i < 4; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Shai, Monday);
        }

        await db.AddPatientWithReadingAsync(Seed.Shai, Monday, status: "entered-in-error"); // typed in by mistake

        var row = (await SummaryAsync(db, Seed.Shai)).Single();

        Assert.Equal(true, row[5]); // only 4 real patients, so still hidden
    }

    [Fact]
    public async Task Each_district_and_each_week_is_counted_on_its_own()
    {
        await using var db = await fixture.StartAsync();
        for (var i = 0; i < 5; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Shai, Monday);
            await db.AddPatientWithReadingAsync(Seed.Shai, Monday.AddDays(7));
        }

        for (var i = 0; i < 6; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Kintampo, Monday.AddDays(3));
        }

        var rows = await SummaryAsync(db);

        Assert.Equal(3, rows.Count);
        Assert.Equal([5L, 5L], rows.Where(r => (string)r[0]! == Seed.Shai).Select(r => (long)r[2]!));
        Assert.Equal(6L, rows.Single(r => (string)r[0]! == Seed.Kintampo)[2]);
    }

    [Theory]
    [InlineData(Roles.Official)]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Clinician)]
    public async Task These_roles_can_read_the_summary(string role)
    {
        await using var db = await fixture.StartAsync();
        await db.AddPatientWithReadingAsync(Seed.Shai, Monday);
        await db.AsAsync(role, Seed.Shai);

        Assert.Single(await SummaryAsync(db));
    }

    [Fact]
    public async Task A_CHV_cannot_read_the_summary()
    {
        await using var db = await fixture.StartAsync();
        await db.AsAsync(Roles.Chv, Seed.Shai);

        var refusal = await db.FailsAsync("SELECT * FROM surveillance_weekly");

        Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);
    }

    [Fact]
    public async Task An_officer_gets_the_counts_but_not_the_patients_behind_them()
    {
        await using var db = await fixture.StartAsync();
        for (var i = 0; i < 5; i++)
        {
            await db.AddPatientWithReadingAsync(Seed.Shai, Monday);
        }

        await db.AsAsync(Roles.Official, Seed.Shai);

        Assert.Equal(5L, (await SummaryAsync(db, Seed.Shai)).Single()[2]);
        Assert.Equal(SqlState.InsufficientPrivilege, (await db.FailsAsync("SELECT * FROM patients")).SqlState);
        Assert.Equal(SqlState.InsufficientPrivilege, (await db.FailsAsync("SELECT * FROM observations")).SqlState);
    }

    [Fact]
    public async Task The_summary_has_no_column_that_could_identify_anyone()
    {
        await using var db = await fixture.StartAsync();

        var columns = await db.RowsAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'surveillance_weekly' ORDER BY ordinal_position");

        Assert.Equal(
            ["district", "week", "patients_seen", "patients_high_bp", "patients_fever", "suppressed"],
            columns.Select(c => (string)c[0]!));
    }
}
