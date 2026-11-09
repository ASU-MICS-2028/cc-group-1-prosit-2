using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;

namespace HealthConnect.RecordsService.Tests;

/// <summary>PUT /api/vitals/{id}: saving a reading the phone took.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class VitalsTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Chv = RecordsDatabase.Chv;
    private static readonly Worker Nurse = RecordsDatabase.Nurse;

    [Fact]
    public async Task A_reading_is_saved_and_comes_back_with_every_number_as_it_was_sent()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutVitalsAsync(id, Vitals(id, patient, height: 162m, notes: "Dizzy this morning"), Chv);

        Assert.Equal(201, reply.Status);
        Assert.Equal(124, reply.Body.GetProperty("systolic").GetInt32());
        Assert.Equal(82, reply.Body.GetProperty("diastolic").GetInt32());
        Assert.Equal(36.8m, reply.Body.GetProperty("temperatureC").GetDecimal());
        Assert.Equal(72, reply.Body.GetProperty("pulse").GetInt32());
        Assert.Equal(64.5m, reply.Body.GetProperty("weightKg").GetDecimal());
        Assert.Equal(162m, reply.Body.GetProperty("heightCm").GetDecimal());
        Assert.Equal("Dizzy this morning", reply.Body.GetProperty("notes").GetString());
        Assert.Equal(patient.ToString(), reply.Body.GetProperty("patientId").GetString());
        Assert.Equal(1, reply.Body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Each_measurement_is_a_coded_observation_and_blood_pressure_shares_a_panel()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        await PutVitalsAsync(id, Vitals(id, patient), Chv);

        var rows = await QueryAsync<(string Type, string Loinc, string Value, string Unit)>(
            "SELECT observation_type AS Type, loinc_code AS Loinc, value AS Value, unit AS Unit FROM observations WHERE vitals_id = @id ORDER BY observation_type", new { id });
        Assert.Equal(
            [
                ("body_temperature", "8310-5", "36.8", "Cel"),
                ("body_weight", "29463-7", "64.5", "kg"),
                ("diastolic_bp", "8462-4", "82", "mm[Hg]"),
                ("heart_rate", "8867-4", "72", "/min"),
                ("systolic_bp", "8480-6", "124", "mm[Hg]"),
            ],
            rows);
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(DISTINCT panel_id) FROM observations WHERE vitals_id = @id AND panel_id IS NOT NULL", new { id }));
        Assert.Equal("chv-042", await ScalarAsync<string>("SELECT recorded_by FROM observations LIMIT 1"));
    }

    [Fact]
    public async Task One_measurement_alone_is_enough()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutVitalsAsync(id, Vitals(id, patient, systolic: null, diastolic: null, pulse: null, weight: null, temperature: 38.4m), Chv);

        Assert.Equal(201, reply.Status);
        Assert.Equal(JsonValueKindNull, reply.Body.GetProperty("systolic").ValueKind);
        Assert.Equal(38.4m, reply.Body.GetProperty("temperatureC").GetDecimal());
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task Sending_the_same_reading_again_saves_it_once()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var first = await PutVitalsAsync(id, Vitals(id, patient), Chv);
        var again = await PutVitalsAsync(id, Vitals(id, patient), Chv);

        Assert.Equal(201, first.Status);
        Assert.Equal(200, again.Status);
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM vitals"));
        Assert.Equal(5, await ScalarAsync<int>("SELECT count(*) FROM observations"));
    }

    [Fact]
    public async Task The_same_reading_arriving_twice_at_the_same_moment_is_saved_once()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PutVitalsAsync(id, Vitals(id, patient), Chv)));

        Assert.All(replies, r => Assert.Contains(r.Status, new[] { 200, 201 }));
        Assert.Equal(1, replies.Count(r => r.Status == 201));
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM vitals"));
        Assert.Equal(5, await ScalarAsync<int>("SELECT count(*) FROM observations"));
    }

    [Fact]
    public async Task A_different_reading_under_the_same_id_is_a_conflict_and_nothing_changes()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutVitalsAsync(id, Vitals(id, patient), Chv);

        var reply = await PutVitalsAsync(id, Vitals(id, patient, systolic: 150, diastolic: 95), Chv);

        Assert.Equal(409, reply.Status);
        Assert.Equal(124, reply.Body.GetProperty("current").GetProperty("systolic").GetInt32());
        Assert.Equal("124", await ScalarAsync<string>("SELECT value FROM observations WHERE observation_type = 'systolic_bp'"));
    }

    public static TheoryData<string, Func<VitalsDto, VitalsDto>> BadReadings => new()
    {
        { "vitals", v => v with { Systolic = null, Diastolic = null, TemperatureC = null, Pulse = null, WeightKg = null, HeightCm = null } },
        { "diastolic", v => v with { Diastolic = null } },
        { "systolic", v => v with { Systolic = null } },
        { "systolic", v => v with { Systolic = 40, Diastolic = 30 } },
        { "systolic", v => v with { Systolic = 300 } },
        { "diastolic", v => v with { Diastolic = 170 } },
        { "systolic", v => v with { Systolic = 80, Diastolic = 90 } },
        { "temperatureC", v => v with { TemperatureC = 98.6m } },
        { "temperatureC", v => v with { TemperatureC = 20m } },
        { "pulse", v => v with { Pulse = 400 } },
        { "weightKg", v => v with { WeightKg = 0m } },
        { "heightCm", v => v with { HeightCm = 3m } },
        { "takenAt", v => v with { TakenAt = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero) } },
        { "notes", v => v with { Notes = new string('x', 2001) } },
        { "id", v => v with { Id = Guid.NewGuid().ToString() } },
        { "patientId", v => v with { PatientId = "not-a-guid" } },
    };

    [Theory]
    [MemberData(nameof(BadReadings))]
    public async Task Something_wrong_in_a_reading_is_a_422_that_names_the_field_and_saves_nothing(string field, Func<VitalsDto, VitalsDto> spoil)
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await PutVitalsAsync(id, spoil(Vitals(id, patient)), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError(field));
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM vitals"));
    }

    [Fact]
    public async Task A_reading_for_a_patient_who_is_not_on_the_server_yet_is_a_422_so_the_phone_keeps_it_and_tries_again()
    {
        var id = Guid.NewGuid();

        var reply = await PutVitalsAsync(id, Vitals(id, Guid.NewGuid()), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError("patientId"));
    }

    [Fact]
    public async Task A_CHV_cannot_add_a_reading_to_a_patient_in_another_district_they_simply_cannot_see_that_patient()
    {
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var id = Guid.NewGuid();

        var reply = await PutVitalsAsync(id, Vitals(id, elsewhere), Chv);

        Assert.Equal(422, reply.Status);
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM vitals"));
    }

    [Fact]
    public async Task A_reading_id_already_used_for_a_patient_the_caller_cannot_see_is_a_conflict_not_a_crash()
    {
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var mine = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await PutVitalsAsync(id, Vitals(id, elsewhere), Nurse);

        var reply = await PutVitalsAsync(id, Vitals(id, mine), Chv);

        Assert.Equal(409, reply.Status);
    }

    [Fact]
    public async Task A_nurse_can_add_a_reading_to_any_patient()
    {
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var id = Guid.NewGuid();

        var reply = await PutVitalsAsync(id, Vitals(id, elsewhere), Nurse);

        Assert.Equal(201, reply.Status);
    }

    [Fact]
    public async Task A_worker_switched_off_is_refused_and_a_token_without_a_worker_is_refused()
    {
        var patient = await SavedPatientAsync();
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'chv-042' RETURNING 1");
        var id = Guid.NewGuid();

        var off = await PutVitalsAsync(id, Vitals(id, patient), Chv);
        var nobody = await ResultReader.RunAsync(await HealthConnect.RecordsService.Features.PutVitals.Handle(
            id, Vitals(id, patient), new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), Sessions, Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HealthConnect.RecordsService.Features.PutVitals>.Instance, CancellationToken.None));

        Assert.Equal(401, off.Status);
        Assert.Equal(401, nobody.Status);
    }

    [Fact]
    public async Task Readings_are_pulled_with_their_numbers_and_only_for_patients_the_caller_may_see()
    {
        var mine = await SavedPatientAsync();
        var theirs = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var own = Guid.NewGuid();
        await PutVitalsAsync(own, Vitals(own, mine), Chv);
        var other = Guid.NewGuid();
        await PutVitalsAsync(other, Vitals(other, theirs), Nurse);

        var chv = await PullVitalsAsync(Chv);
        var nurse = await PullVitalsAsync(Nurse);

        Assert.Equal(1, chv.Body.GetArrayLength());
        Assert.Equal(124, chv.Body[0].GetProperty("systolic").GetInt32());
        Assert.Equal(2, nurse.Body.GetArrayLength());
    }

    [Fact]
    public async Task Pulling_readings_after_a_moment_returns_only_the_newer_ones()
    {
        var patient = await SavedPatientAsync();
        var older = Guid.NewGuid();
        await PutVitalsAsync(older, Vitals(older, patient), Chv);
        await Task.Delay(30);
        var middle = DateTimeOffset.UtcNow;
        await Task.Delay(30);
        var newer = Guid.NewGuid();
        await PutVitalsAsync(newer, Vitals(newer, patient), Chv);

        var after = await PullVitalsAsync(Nurse, middle);

        Assert.Equal(1, after.Body.GetArrayLength());
        Assert.Equal(newer.ToString(), after.Body[0].GetProperty("id").GetString());
    }
}
