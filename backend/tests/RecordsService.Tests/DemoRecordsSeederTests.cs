using HealthConnect.RecordsService.Demo;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.RecordsService.Tests;

/// <summary>The invented patients, readings, visits and referrals that fill a demo database so every screen has something to show.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class DemoRecordsSeederTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private async Task SeedAsync(bool enabled = true, string? sampleData = null)
    {
        var settings = new Dictionary<string, string?> { ["Demo:Enabled"] = enabled.ToString() };
        if (sampleData is not null)
        {
            settings["Demo:SampleData"] = sampleData;
        }

        var seeder = new DemoRecordsSeeder(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), Sessions, TimeProvider.System, NullLogger<DemoRecordsSeeder>.Instance);
        await seeder.SeedAsync(null!, CancellationToken.None);
    }

    private Task<int> CountAsync(string table) => ScalarAsync<int>($"SELECT count(*) FROM {table}");

    [Fact]
    public async Task Seeding_fills_the_four_districts_with_invented_patients_and_readings()
    {
        await SeedAsync();

        var perDistrict = await QueryAsync<(string District, int Patients)>("SELECT district AS District, count(*)::int AS Patients FROM patients GROUP BY district ORDER BY district");
        Assert.Equal([("Kintampo North", 10), ("Sawla-Tuna-Kalba", 3), ("Shai-Osudoku", 12), ("Tamale Metropolitan", 8)], perDistrict);
        Assert.Equal(33, await CountAsync("patients WHERE family_name LIKE '%-Demo'"));
        Assert.True(await CountAsync("vitals") > 100);
        Assert.True(await CountAsync("observations") > 400);
        Assert.Equal(33, await CountAsync("consents"));
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing_the_second_time_and_changes_nothing()
    {
        await SeedAsync();
        var counts = new[] { await CountAsync("patients"), await CountAsync("vitals"), await CountAsync("appointments"), await CountAsync("observations") };
        var referral = await ScalarAsync<string>("SELECT referral_status FROM appointments WHERE reason LIKE 'Fever for three days%'");

        await SeedAsync();

        Assert.Equal(counts, new[] { await CountAsync("patients"), await CountAsync("vitals"), await CountAsync("appointments"), await CountAsync("observations") });
        Assert.Equal(referral, await ScalarAsync<string>("SELECT referral_status FROM appointments WHERE reason LIKE 'Fever for three days%'"));
    }

    [Fact]
    public async Task The_nurses_inbox_has_new_accepted_seen_and_declined_referrals()
    {
        await SeedAsync();

        var states = await QueryAsync<(string Status, string? HandledBy)>(
            "SELECT referral_status AS Status, handled_by AS HandledBy FROM appointments WHERE type = 'referral' ORDER BY referral_status");

        Assert.Equal([("accepted", "nurse-17"), ("declined", "nurse-17"), ("requested", null), ("seen", "nurse-17")], states);
    }

    [Fact]
    public async Task The_CHV_has_upcoming_overdue_done_and_missed_visits()
    {
        await SeedAsync();

        var statuses = await QueryAsync<string>("SELECT status FROM appointments WHERE type = 'follow_up' GROUP BY status ORDER BY status");
        var overdue = await CountAsync("appointments WHERE status = 'scheduled' AND scheduled_at < now()");
        var upcoming = await CountAsync("appointments WHERE status = 'scheduled' AND scheduled_at > now()");

        Assert.Equal(["completed", "missed", "scheduled"], statuses);
        Assert.True(overdue >= 1);
        Assert.True(upcoming >= 3);
    }

    [Fact]
    public async Task The_CHV_sees_only_the_shai_osudoku_patients_and_the_nurse_sees_everyone()
    {
        await SeedAsync();

        var chv = await PullPatientsAsync(RecordsDatabase.Chv);
        var nurse = await PullPatientsAsync(RecordsDatabase.Nurse);

        Assert.Equal(12, chv.Body.GetArrayLength());
        Assert.Equal(33, nurse.Body.GetArrayLength());
    }

    [Fact]
    public async Task The_district_summary_shows_real_counts_and_hides_the_district_too_small_to_show()
    {
        await SeedAsync();

        var reply = await ResultReader.RunAsync(await GetSurveillance.Handle(As(RecordsDatabase.Officer), Sessions, CancellationToken.None));

        var rows = reply.Body.EnumerateArray().ToList();
        string District(System.Text.Json.JsonElement r) => r.GetProperty("district").GetString()!;
        var shown = rows.Where(r => !r.GetProperty("suppressed").GetBoolean()).ToList();
        Assert.True(shown.Count(r => District(r) == "Shai-Osudoku") >= 6);
        Assert.True(shown.Count(r => District(r) == "Kintampo North") >= 6);
        Assert.True(shown.Count(r => District(r) == "Tamale Metropolitan") >= 6);
        Assert.All(rows.Where(r => District(r) == "Sawla-Tuna-Kalba"), r => Assert.True(r.GetProperty("suppressed").GetBoolean()));
        // Kintampo North's fever count rises towards the present: the outbreak signal the demo is meant to show.
        var fevers = shown.Where(r => District(r) == "Kintampo North").OrderBy(r => r.GetProperty("week").GetString()).Select(r => r.GetProperty("patients_fever").GetInt64()).ToList();
        Assert.True(fevers.Max() >= 4);
        Assert.True(fevers[^1] > fevers[0]);
    }

    [Fact]
    public async Task Nothing_is_seeded_when_demo_mode_is_off_or_sample_data_is_off()
    {
        await SeedAsync(enabled: false);
        await SeedAsync(enabled: true, sampleData: "false");

        Assert.Equal(0, await CountAsync("patients"));
    }

    [Fact]
    public async Task Nothing_is_seeded_when_a_demo_worker_is_switched_off()
    {
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'nurse-17' RETURNING 1");

        await SeedAsync();

        Assert.Equal(0, await CountAsync("patients"));
    }
}
