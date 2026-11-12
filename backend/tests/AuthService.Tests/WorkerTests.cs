using System.Security.Claims;
using HealthConnect.AuthService.Features;
using HealthConnect.AuthService.Tests.Support;
using HealthConnect.SharedLibrary;
using HealthConnect.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.AuthService.Tests;

/// <summary>The Health workers page: who adds, changes and switches off workers, and where.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class WorkerTests(AuthDatabase database) : AuthTestBase(database)
{
    private static ClaimsPrincipal As(string id, string role, string district = "Shai-Osudoku") =>
        new(new ClaimsIdentity([new Claim("sub", id), new Claim("role", role), new Claim("name", id), new Claim("district", district)], "test"));

    private static readonly ClaimsPrincipal Officer = As("ghs-officer-07", "district_officer");
    private static readonly ClaimsPrincipal Admin = As("auditor-02", "admin");

    private async Task<Reply> AddAsync(ClaimsPrincipal who, string name, string phone, string role, string? district = null)
    {
        await using var db = Database.NewContext();
        return await ResultReader.RunAsync(await PostWorker.Handle(
            new NewWorkerRequest(name, phone, role, district), who, db, Time, NullLogger<PostWorker>.Instance, CancellationToken.None));
    }

    private async Task<Reply> ChangeAsync(ClaimsPrincipal who, string id, WorkerChangeRequest change)
    {
        await using var db = Database.NewContext();
        return await ResultReader.RunAsync(await PatchWorker.Handle(id, change, who, db, NullLogger<PatchWorker>.Instance, CancellationToken.None));
    }

    private async Task<Reply> ListAsync(ClaimsPrincipal who)
    {
        await using var db = Database.NewContext();
        return await ResultReader.RunAsync(await GetWorkers.Handle(who, db, CancellationToken.None));
    }

    [Fact]
    public async Task The_officer_adds_a_chv_who_can_then_sign_in_with_a_code()
    {
        await AddWorkerAsync("ghs-officer-07", "+233200000104", "district_officer");

        var added = await AddAsync(Officer, "Yaa Asantewaa", "024 555 0001", "chv", district: "Somewhere Else");

        Assert.Equal(201, added.Status);
        Assert.Equal("Shai-Osudoku", added.Body.GetProperty("district").GetString());   // always the officer's own district
        Assert.Equal("+233245550001", added.Body.GetProperty("phone").GetString());
        Assert.StartsWith("chv-", added.Body.GetProperty("id").GetString());
        Assert.Equal("ghs-officer-07", added.Body.GetProperty("addedBy").GetString());

        Assert.Equal(204, (await RequestCodeAsync("0245550001")).Status);
        var signedIn = await VerifyAsync("0245550001", Sms.LastCode);
        Assert.Equal("chv", signedIn.Body.GetProperty("user").GetProperty("role").GetString());

        await using var db = Database.NewContext();
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM access_log WHERE entity = 'worker' AND detail LIKE 'added chv%'").SingleAsync());
    }

    [Fact]
    public async Task The_officer_cannot_make_officers_or_admins_but_the_admin_can_anywhere()
    {
        Assert.Equal(422, (await AddAsync(Officer, "Kofi Boss", "024 555 0002", "admin")).Status);
        Assert.Equal(422, (await AddAsync(Officer, "Kofi Boss", "024 555 0002", "district_officer")).Status);

        var officer = await AddAsync(Admin, "Ama Officer", "024 555 0003", "district_officer", "Kintampo North");
        Assert.Equal(201, officer.Status);
        Assert.Equal("Kintampo North", officer.Body.GetProperty("district").GetString());
    }

    [Fact]
    public async Task A_number_already_in_use_is_refused()
    {
        await AddWorkerAsync("chv-042", "+233200000101");
        await using (var db = Database.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('patient-233598256428', '+233598256428', 'Abena', 'patient', 'Shai-Osudoku')");
        }

        var worker = await AddAsync(Officer, "Someone", "020 000 0101", "chv");
        var patient = await AddAsync(Officer, "Someone", "059 825 6428", "chv");

        Assert.Equal((409, "number_in_use"), (worker.Status, worker.Error));
        Assert.Equal((409, "number_is_a_patients"), (patient.Status, patient.Error));
    }

    [Fact]
    public async Task Switching_a_worker_off_stops_their_sign_in_and_only_their_own_district_is_managed()
    {
        await AddWorkerAsync("chv-042", "+233200000101");
        await AddWorkerAsync("chv-099", "+233200000199");
        await using (var db = Database.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE users SET district = 'Kintampo North' WHERE id = 'chv-099'");
        }

        var off = await ChangeAsync(Officer, "chv-042", new WorkerChangeRequest(null, null, false));
        Assert.Equal(200, off.Status);
        Assert.False(off.Body.GetProperty("active").GetBoolean());
        Assert.Equal(404, (await RequestCodeAsync("020 000 0101")).Status);

        Assert.Equal(404, (await ChangeAsync(Officer, "chv-099", new WorkerChangeRequest(null, null, false))).Status);
        var listed = (await ListAsync(Officer)).Body.EnumerateArray().Select(w => w.GetProperty("id").GetString()).ToList();
        Assert.Equal(["chv-042"], listed);
        Assert.Equal(2, (await ListAsync(Admin)).Body.GetArrayLength());
    }

    [Fact]
    public async Task Nobody_changes_their_own_account_and_patients_are_never_workers()
    {
        await AddWorkerAsync("ghs-officer-07", "+233200000104", "district_officer");
        await using (var db = Database.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("INSERT INTO users (id, phone, name, role, district) VALUES ('patient-233598256428', '+233598256428', 'Abena', 'patient', 'Shai-Osudoku')");
        }

        var self = await ChangeAsync(Officer, "ghs-officer-07", new WorkerChangeRequest(null, null, false));
        Assert.Equal((403, "not_yourself"), (self.Status, self.Error));
        Assert.Equal(404, (await ChangeAsync(Officer, "patient-233598256428", new WorkerChangeRequest(null, "chv", null))).Status);
        Assert.DoesNotContain((await ListAsync(Officer)).Body.EnumerateArray(), w => w.GetProperty("role").GetString() == "patient");
    }
}
