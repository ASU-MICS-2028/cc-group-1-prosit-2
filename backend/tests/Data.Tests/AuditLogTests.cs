using HealthConnect.Data.Tests.Support;

namespace HealthConnect.Data.Tests;

/// <summary>The audit log (FHIR AuditEvent): append-only, hash-chained, and readable only by the auditor.</summary>
[IntegrationTest]
[Collection(SchemaCollection.Name)]
public sealed class AuditLogTests(SchemaFixture fixture)
{
    [Fact]
    public async Task Entries_are_chained_and_the_chain_verifies()
    {
        await using var db = await fixture.StartAsync();
        await db.AddAuditEntryAsync("chv-1");
        await db.AddAuditEntryAsync("chv-2");
        await db.AddAuditEntryAsync("chv-3");

        var first = (await db.RowsAsync("SELECT prev_hash, entry_hash FROM access_log ORDER BY id")).First();
        var all = await db.RowsAsync("SELECT prev_hash, entry_hash FROM access_log ORDER BY id");
        var check = (await db.RowsAsync("SELECT first_bad_id, entries_checked FROM verify_access_log()")).Single();

        Assert.NotNull(first[1]);
        for (var i = 1; i < all.Count; i++)
        {
            Assert.Equal(all[i - 1][1], all[i][0]); // each entry carries the hash of the one before it
        }

        Assert.Null(check[0]);
        Assert.Equal(3L, check[1]);
    }

    [Fact]
    public async Task An_entry_cannot_be_changed_or_removed()
    {
        await using var db = await fixture.StartAsync();
        await db.AddAuditEntryAsync();

        var update = await db.FailsAsync("UPDATE access_log SET accessed_by = 'someone-else'");
        var delete = await db.FailsAsync("DELETE FROM access_log");
        var truncate = await db.FailsAsync("TRUNCATE access_log");

        foreach (var refusal in new[] { update, delete, truncate })
        {
            Assert.Equal(SqlState.RaisedByTrigger, refusal.SqlState);
            Assert.Contains("append-only", refusal.MessageText);
        }
    }

    [Fact]
    public async Task Tampering_by_someone_who_switches_the_protection_off_is_found_where_it_happened()
    {
        await using var db = await fixture.StartAsync();
        await db.AddAuditEntryAsync("chv-1");
        await db.AddAuditEntryAsync("chv-2");
        await db.AddAuditEntryAsync("chv-3");
        var secondId = await db.ScalarAsync<int>("SELECT id FROM access_log WHERE accessed_by = 'chv-2'");

        // an administrator with full rights can switch the trigger off and rewrite history ...
        await db.ExecAsync("ALTER TABLE access_log DISABLE TRIGGER access_log_no_update_delete");
        await db.ExecAsync("UPDATE access_log SET accessed_by = 'nobody' WHERE accessed_by = 'chv-2'");
        await db.ExecAsync("ALTER TABLE access_log ENABLE TRIGGER access_log_no_update_delete");

        // ... but the chain no longer adds up, and says which entry is the first that does not
        var check = (await db.RowsAsync("SELECT first_bad_id, entries_checked FROM verify_access_log()")).Single();
        Assert.Equal(secondId, check[0]);
    }

    [Fact]
    public async Task A_deleted_entry_breaks_the_chain_too()
    {
        await using var db = await fixture.StartAsync();
        await db.AddAuditEntryAsync("chv-1");
        await db.AddAuditEntryAsync("chv-2");
        await db.AddAuditEntryAsync("chv-3");
        var thirdId = await db.ScalarAsync<int>("SELECT id FROM access_log WHERE accessed_by = 'chv-3'");

        await db.ExecAsync("ALTER TABLE access_log DISABLE TRIGGER access_log_no_update_delete");
        await db.ExecAsync("DELETE FROM access_log WHERE accessed_by = 'chv-2'");
        await db.ExecAsync("ALTER TABLE access_log ENABLE TRIGGER access_log_no_update_delete");

        var check = (await db.RowsAsync("SELECT first_bad_id FROM verify_access_log()")).Single();
        Assert.Equal(thirdId, check[0]);
    }

    [Theory]
    [InlineData(Roles.Chv)]
    [InlineData(Roles.Clinician)]
    [InlineData(Roles.Official)]
    [InlineData(Roles.Auditor)]
    public async Task Every_role_can_add_an_entry(string role)
    {
        await using var db = await fixture.StartAsync();
        await db.AsAsync(role, Seed.Shai);

        await db.ExecAsync("INSERT INTO access_log (accessed_by, role, action) VALUES ('someone', 'x', 'read')");

        await db.AsOwnerAsync();
        Assert.Equal(1, await db.CountAsync("SELECT count(*) FROM access_log"));
    }

    [Theory]
    [InlineData(Roles.Chv)]
    [InlineData(Roles.Clinician)]
    [InlineData(Roles.Official)]
    public async Task Only_the_auditor_can_read_the_log(string role)
    {
        await using var db = await fixture.StartAsync();
        await db.AddAuditEntryAsync();
        await db.AsAsync(role, Seed.Shai);

        var refusal = await db.FailsAsync("SELECT * FROM access_log");

        Assert.Equal(SqlState.InsufficientPrivilege, refusal.SqlState);
    }

    [Fact]
    public async Task The_auditor_reads_the_log_and_runs_the_check_nobody_else_can_run()
    {
        await using var db = await fixture.StartAsync();
        await db.AddAuditEntryAsync("chv-1");
        await db.AsAsync(Roles.Auditor, Seed.Shai);

        Assert.Equal(1, await db.CountAsync("SELECT count(*) FROM access_log"));
        var check = (await db.RowsAsync("SELECT first_bad_id, entries_checked FROM verify_access_log()")).Single();
        Assert.Null(check[0]);

        await db.AsAsync(Roles.Chv, Seed.Shai);
        Assert.Equal(SqlState.InsufficientPrivilege, (await db.FailsAsync("SELECT * FROM verify_access_log()")).SqlState);
    }

    [Fact]
    public async Task An_unknown_action_or_outcome_is_refused()
    {
        await using var db = await fixture.StartAsync();

        var action = await db.FailsAsync("INSERT INTO access_log (accessed_by, action) VALUES ('x', 'peek')");
        var outcome = await db.FailsAsync("INSERT INTO access_log (accessed_by, action, outcome) VALUES ('x', 'read', 'maybe')");
        var purpose = await db.FailsAsync("INSERT INTO access_log (accessed_by, action, purpose_of_use) VALUES ('x', 'read', 'CURIOSITY')");

        Assert.All([action, outcome, purpose], refusal => Assert.Equal(SqlState.CheckViolation, refusal.SqlState));
    }

    [Fact]
    public async Task The_log_keeps_an_entry_about_a_patient_that_does_not_exist()
    {
        // A refused attempt on a missing or hidden record must still be recorded (so there is no foreign key on patient_id).
        await using var db = await fixture.StartAsync();

        await db.ExecAsync("INSERT INTO access_log (accessed_by, role, patient_id, action, outcome) VALUES ('chv-9', 'chv', 999999, 'read', 'not-found')");

        Assert.Equal(1, await db.CountAsync("SELECT count(*) FROM access_log WHERE patient_id = 999999"));
    }
}
