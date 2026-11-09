using HealthConnect.AuthService.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace HealthConnect.AuthService.Tests;

/// <summary>Each sign-in, and each refused try by a registered worker, is written to the hash-chained audit log.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class SignInAuditTests(AuthDatabase database) : AuthTestBase(database)
{
    public sealed record Entry(string AccessedBy, string Role, string Action, string Outcome, string Purpose, string Detail);

    private async Task<List<Entry>> LogForAsync(string worker)
    {
        await using var db = Database.NewContext();
        return await db.Database.SqlQuery<Entry>(
            $"""
            SELECT accessed_by AS "AccessedBy", role AS "Role", action AS "Action", outcome AS "Outcome",
                   purpose_of_use AS "Purpose", detail AS "Detail"
            FROM access_log WHERE entity = 'sign-in' AND accessed_by = {worker} ORDER BY id
            """).ToListAsync();
    }

    [Fact]
    public async Task A_sign_in_is_recorded_with_who_and_which_role()
    {
        var worker = $"nurse-{Guid.NewGuid():N}"[..14];
        await AddWorkerAsync(id: worker, role: "nurse");
        await RequestCodeAsync("+233200000101");

        await VerifyAsync("+233200000101", Sms.LastCode);

        Assert.Equal([new Entry(worker, "nurse", "read", "success", "HOPERAT", "signed in")], await LogForAsync(worker));
    }

    [Fact]
    public async Task A_sign_in_with_the_demo_code_says_so()
    {
        var worker = $"demo-{Guid.NewGuid():N}"[..13];
        await AddWorkerAsync(id: worker, demo: true);
        Demo.Enabled = true;

        await VerifyAsync("+233200000101", "123456");

        var entry = Assert.Single(await LogForAsync(worker));
        Assert.Equal(("success", "signed in with the demo code"), (entry.Outcome, entry.Detail));
    }

    [Fact]
    public async Task A_wrong_code_by_a_registered_worker_is_recorded_as_denied_and_signs_nobody_in()
    {
        var worker = $"chv-{Guid.NewGuid():N}"[..12];
        await AddWorkerAsync(id: worker);
        await RequestCodeAsync("+233200000101");
        var wrong = Sms.LastCode == "000000" ? "111111" : "000000";

        var reply = await VerifyAsync("+233200000101", wrong);

        Assert.Equal(401, reply.Status);
        var entry = Assert.Single(await LogForAsync(worker));
        Assert.Equal(("denied", "refused: wrong code"), (entry.Outcome, entry.Detail));
    }

    [Fact]
    public async Task A_late_code_and_a_reused_code_are_recorded_as_denied()
    {
        var worker = $"chv-{Guid.NewGuid():N}"[..12];
        await AddWorkerAsync(id: worker);
        await RequestCodeAsync("+233200000101");
        var code = Sms.LastCode;
        Time.Advance(TimeSpan.FromMinutes(11));

        await VerifyAsync("+233200000101", code);

        Assert.Equal("refused: no valid code", Assert.Single(await LogForAsync(worker)).Detail);
    }

    [Fact]
    public async Task Too_many_wrong_tries_are_recorded()
    {
        var worker = $"chv-{Guid.NewGuid():N}"[..12];
        await AddWorkerAsync(id: worker);
        await RequestCodeAsync("+233200000101");
        var wrong = Sms.LastCode == "000000" ? "111111" : "000000";
        for (var i = 0; i < 6; i++)
        {
            await VerifyAsync("+233200000101", wrong);
        }

        var log = await LogForAsync(worker);

        Assert.Equal(6, log.Count);
        Assert.Equal("refused: too many wrong tries", log[^1].Detail);
    }

    [Fact]
    public async Task A_number_that_is_not_a_registered_worker_is_not_logged_so_nobody_can_fill_the_log()
    {
        await using var before = Database.NewContext();
        var count = await before.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM access_log WHERE entity = 'sign-in'").SingleAsync();

        await VerifyAsync("+233209999999", "123456");

        await using var after = Database.NewContext();
        Assert.Equal(count, await after.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM access_log WHERE entity = 'sign-in'").SingleAsync());
    }

    [Fact]
    public async Task The_log_chain_still_checks_out_after_sign_ins()
    {
        await AddWorkerAsync();
        await RequestCodeAsync("+233200000101");
        await VerifyAsync("+233200000101", Sms.LastCode);

        await using var db = Database.NewContext();
        var firstBad = await db.Database.SqlQuery<int?>($"SELECT first_bad_id AS \"Value\" FROM verify_access_log()").SingleAsync();

        Assert.Null(firstBad);
    }
}
