using HealthConnect.AuthService.Tests.Support;
using HealthConnect.SharedLibrary;
using HealthConnect.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HealthConnect.AuthService.Tests;

/// <summary>Patients sign in with the number on their record, with the same code-by-SMS as the workers.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class PatientSignInTests(AuthDatabase database) : AuthTestBase(database)
{
    private async Task PatientAsync(string name, string phone)
    {
        await using var db = Database.NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO patients (full_name, district, phone) VALUES ({name}, 'Shai-Osudoku', {phone})");
    }

    [Fact]
    public async Task A_patient_signs_in_with_the_number_on_their_record()
    {
        await PatientAsync("Abena Owusu", "+233598256428");

        Assert.Equal(204, (await RequestCodeAsync("059 825 6428")).Status);
        var (to, message) = Assert.Single(Sms.Sent);
        Assert.Equal("+233598256428", to);
        Assert.Contains(Sms.LastCode, message);

        var reply = await VerifyAsync("059 825 6428", Sms.LastCode);

        Assert.Equal(200, reply.Status);
        var user = reply.Body.GetProperty("user");
        Assert.Equal("patient", user.GetProperty("role").GetString());
        Assert.Equal("Abena Owusu", user.GetProperty("name").GetString());
        Assert.Equal("patient-233598256428", user.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_household_sharing_one_phone_has_one_account()
    {
        await PatientAsync("Abena Owusu", "+233598256428");
        await PatientAsync("Kojo Owusu", "+233598256428");

        await RequestCodeAsync("0598256428");
        var reply = await VerifyAsync("0598256428", Sms.LastCode);

        Assert.Equal("Abena Owusu (+1)", reply.Body.GetProperty("user").GetProperty("name").GetString());
        await using var db = Database.NewContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == "patient"));
    }

    [Fact]
    public async Task A_health_workers_number_is_always_the_workers()
    {
        await AddWorkerAsync("chv-042", "+233200000101");
        await PatientAsync("Esi Mensah", "+233200000101");

        await RequestCodeAsync("020 000 0101");
        var reply = await VerifyAsync("020 000 0101", Sms.LastCode);

        Assert.Equal("chv", reply.Body.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_number_on_no_record_is_unknown_and_a_number_taken_off_its_record_stops_working()
    {
        Assert.Equal(404, (await RequestCodeAsync("024 000 0000")).Status);

        await PatientAsync("Abena Owusu", "+233598256428");
        await RequestCodeAsync("0598256428");
        await using (var db = Database.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE patients SET phone = NULL");
        }

        Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(404, (await RequestCodeAsync("0598256428")).Status);
        Assert.Equal(401, (await VerifyAsync("0598256428", Sms.LastCode)).Status);
    }

    [Fact]
    public async Task The_database_accepts_a_patient_account_and_still_refuses_made_up_roles()
    {
        await using var db = Database.NewContext();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('p', '+233200000998', 'P', 'patient', 'D')");
        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('x', '+233200000999', 'X', 'superuser', 'D')"));
        Assert.Equal("users_role_allowed", refused.ConstraintName);
    }
}
