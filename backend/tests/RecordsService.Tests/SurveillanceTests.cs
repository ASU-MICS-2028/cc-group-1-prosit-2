using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Tests.Support;

namespace HealthConnect.RecordsService.Tests;

/// <summary>GET /api/surveillance/weekly: the anonymised counts the district officer sees. A cell under 5 patients is hidden.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class SurveillanceTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Nurse = RecordsDatabase.Nurse;

    private async Task SeeAsync(int patients, string district, int highBp = 0, int fever = 0)
    {
        for (var i = 0; i < patients; i++)
        {
            var patient = await SavedPatientAsync(Nurse, district);
            var id = Guid.NewGuid();
            var reading = Vitals(id, patient, systolic: i < highBp ? 150 : 118, diastolic: i < highBp ? 96 : 76, temperature: i < fever ? 38.6m : 36.7m);
            Assert.Equal(201, (await PutVitalsAsync(id, reading, Nurse)).Status);
        }
    }

    private async Task<Reply> WeeklyAsync(Worker who) =>
        await ResultReader.RunAsync(await GetSurveillance.Handle(As(who), Sessions, CancellationToken.None));

    [Fact]
    public async Task A_district_week_with_five_or_more_patients_shows_the_counts_in_the_names_the_app_reads()
    {
        await SeeAsync(5, "Shai-Osudoku", highBp: 3, fever: 2);

        var reply = await WeeklyAsync(RecordsDatabase.Officer);

        Assert.Equal(200, reply.Status);
        Assert.Equal(1, reply.Body.GetArrayLength());
        var row = reply.Body[0];
        Assert.Equal("Shai-Osudoku", row.GetProperty("district").GetString());
        Assert.Equal("2026-11-09", row.GetProperty("week").GetString());
        Assert.Equal(5, row.GetProperty("patients_seen").GetInt64());
        Assert.Equal(3, row.GetProperty("patients_high_bp").GetInt64());
        Assert.Equal(2, row.GetProperty("patients_fever").GetInt64());
        Assert.False(row.GetProperty("suppressed").GetBoolean());
    }

    [Fact]
    public async Task A_district_week_with_fewer_than_five_patients_is_hidden_so_nobody_can_be_picked_out()
    {
        await SeeAsync(4, "Kintampo North", highBp: 4, fever: 4);

        var reply = await WeeklyAsync(RecordsDatabase.Officer);

        var row = reply.Body[0];
        Assert.Equal("Kintampo North", row.GetProperty("district").GetString());
        Assert.True(row.GetProperty("suppressed").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("patients_seen").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("patients_high_bp").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("patients_fever").ValueKind);
    }

    [Fact]
    public async Task The_officer_sees_every_district_and_the_auditor_sees_the_same_numbers()
    {
        await SeeAsync(5, "Shai-Osudoku");
        await SeeAsync(6, "Kintampo North");

        var officer = await WeeklyAsync(RecordsDatabase.Officer);
        var auditor = await WeeklyAsync(RecordsDatabase.Admin);

        Assert.Equal(
            ["Kintampo North", "Shai-Osudoku"],
            officer.Body.EnumerateArray().Select(r => r.GetProperty("district").GetString()!).ToArray());
        Assert.Equal(officer.Body.GetRawText(), auditor.Body.GetRawText());
    }

    [Fact]
    public async Task No_patient_data_is_in_the_answer()
    {
        await SeeAsync(5, "Shai-Osudoku");

        var reply = await WeeklyAsync(RecordsDatabase.Officer);

        Assert.Equal(
            ["district", "patients_fever", "patients_high_bp", "patients_seen", "suppressed", "week"],
            reply.Body[0].EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task With_no_readings_at_all_the_answer_is_an_empty_list()
    {
        var reply = await WeeklyAsync(RecordsDatabase.Officer);

        Assert.Equal(200, reply.Status);
        Assert.Equal(0, reply.Body.GetArrayLength());
    }

    [Fact]
    public async Task A_worker_switched_off_and_a_token_without_a_worker_are_refused()
    {
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'ghs-officer-07' RETURNING 1");

        var off = await WeeklyAsync(RecordsDatabase.Officer);
        var nobody = await ResultReader.RunAsync(await GetSurveillance.Handle(
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), Sessions, CancellationToken.None));

        Assert.Equal(401, off.Status);
        Assert.Equal(401, nobody.Status);
    }
}
