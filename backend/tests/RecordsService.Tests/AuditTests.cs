using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.RecordsService.Tests;

/// <summary>The audit trail: the phone uploads who saw or changed what, the auditor reads it and checks it was not tampered with.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class AuditTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Chv = RecordsDatabase.Chv;
    private static readonly Worker Admin = RecordsDatabase.Admin;

    private AuditEventDto Event(Guid? id = null, string action = "view", string entity = "patient", Guid? patient = null, string outcome = "success", string purpose = "TREAT", string? detail = null) => new(
        Id: (id ?? Guid.NewGuid()).ToString(),
        At: Start.AddMinutes(-3),
        Action: action,
        Entity: entity,
        EntityId: patient?.ToString(),
        PatientId: patient?.ToString(),
        Outcome: outcome,
        Purpose: purpose,
        Detail: detail);

    private async Task<Reply> UploadAsync(Worker who, params AuditEventDto[] events) =>
        await ResultReader.RunAsync(await PostAuditEvents.Handle(
            new AuditUploadRequest([.. events]), As(who), null, Sessions, Time, NullLogger<PostAuditEvents>.Instance, CancellationToken.None));

    private async Task<Reply> ReadAsync(Worker who, Guid? patient = null, int? limit = null) =>
        await ResultReader.RunAsync(await GetAuditEvents.Handle(As(who), Sessions, CancellationToken.None, patient, limit));

    [Fact]
    public async Task Uploaded_events_are_written_to_the_log_as_the_signed_in_worker()
    {
        var patient = Guid.NewGuid();
        var viewed = Guid.NewGuid();
        var created = Guid.NewGuid();

        var reply = await UploadAsync(Chv, Event(viewed, "view", patient: patient), Event(created, "create", "vitals", patient));

        Assert.Equal(204, reply.Status);
        var rows = await QueryAsync<(string By, string Role, string Action, string Outcome, string Entity)>(
            "SELECT accessed_by AS By, role AS Role, action AS Action, outcome AS Outcome, entity AS Entity FROM access_log WHERE client_event_id = ANY(@ids) ORDER BY id",
            new { ids = new[] { viewed, created } });
        Assert.Equal([("chv-042", "chv", "read", "success", "patient"), ("chv-042", "chv", "write", "success", "vitals")], rows);
        Assert.Equal(patient, await ScalarAsync<Guid>("SELECT patient_uuid FROM access_log WHERE client_event_id = @viewed", new { viewed }));
    }

    [Theory]
    [InlineData("view", "read")]
    [InlineData("create", "write")]
    [InlineData("update", "write")]
    [InlineData("denied", "read")]
    public async Task Each_kind_of_access_is_a_read_or_a_write_in_the_log(string appAction, string logAction)
    {
        var id = Guid.NewGuid();

        await UploadAsync(Chv, Event(id, appAction, outcome: appAction == "denied" ? "denied" : "success"));

        Assert.Equal(logAction, await ScalarAsync<string>("SELECT action FROM access_log WHERE client_event_id = @id", new { id }));
    }

    [Fact]
    public async Task A_refused_visit_is_logged_as_denied_even_if_the_phone_said_success()
    {
        var id = Guid.NewGuid();

        await UploadAsync(Chv, Event(id, "denied", "page", detail: "/audit", outcome: "success"));

        Assert.Equal("denied", await ScalarAsync<string>("SELECT outcome FROM access_log WHERE client_event_id = @id", new { id }));
        Assert.Equal("/audit", await ScalarAsync<string>("SELECT detail FROM access_log WHERE client_event_id = @id", new { id }));
    }

    [Fact]
    public async Task Sending_the_same_events_again_stores_them_once()
    {
        var id = Guid.NewGuid();
        await UploadAsync(Chv, Event(id));

        var again = await UploadAsync(Chv, Event(id));

        Assert.Equal(204, again.Status);
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM access_log WHERE client_event_id = @id", new { id }));
    }

    [Theory]
    [InlineData("nurse-17")]
    [InlineData("ghs-officer-07")]
    [InlineData("auditor-02")]
    public async Task Every_role_can_upload_its_own_events(string workerId)
    {
        var who = new[] { Chv, RecordsDatabase.Nurse, RecordsDatabase.Officer, Admin }.Single(w => w.Id == workerId);
        var id = Guid.NewGuid();

        var reply = await UploadAsync(who, Event(id));

        Assert.Equal(204, reply.Status);
        Assert.Equal(who.Id, await ScalarAsync<string>("SELECT accessed_by FROM access_log WHERE client_event_id = @id", new { id }));
        Assert.Equal(who.Role, await ScalarAsync<string>("SELECT role FROM access_log WHERE client_event_id = @id", new { id }));
    }

    public static TheoryData<string, Func<AuditEventDto, AuditEventDto>> BadEvents => new()
    {
        { "events[0].id", e => e with { Id = "nope" } },
        { "events[0].action", e => e with { Action = "delete" } },
        { "events[0].outcome", e => e with { Outcome = "maybe" } },
        { "events[0].purpose", e => e with { Purpose = "SELL" } },
        { "events[0].entity", e => e with { Entity = "" } },
        { "events[0].entity", e => e with { Entity = new string('x', 41) } },
        { "events[0].entityId", e => e with { EntityId = new string('x', 101) } },
        { "events[0].patientId", e => e with { PatientId = "not-a-guid" } },
        { "events[0].detail", e => e with { Detail = new string('x', 501) } },
        { "events[0].at", e => e with { At = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero) } },
        { "events[0].at", e => e with { At = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero) } },
    };

    [Theory]
    [MemberData(nameof(BadEvents))]
    public async Task A_bad_event_is_a_422_that_names_it_and_nothing_in_the_batch_is_saved(string field, Func<AuditEventDto, AuditEventDto> spoil)
    {
        var good = Guid.NewGuid();

        var reply = await UploadAsync(Chv, spoil(Event()), Event(good));

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError(field));
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM access_log WHERE client_event_id = @good", new { good }));
    }

    [Fact]
    public async Task An_empty_batch_and_a_huge_batch_are_both_refused()
    {
        var empty = await UploadAsync(Chv);
        var huge = await UploadAsync(Chv, [.. Enumerable.Range(0, Audit.MaxBatch + 1).Select(_ => Event())]);

        Assert.Equal(422, empty.Status);
        Assert.NotNull(empty.FieldError("events"));
        Assert.Equal(422, huge.Status);
        Assert.NotNull(huge.FieldError("events"));
    }

    [Fact]
    public async Task A_worker_switched_off_cannot_upload()
    {
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'chv-042' RETURNING 1");

        var reply = await UploadAsync(Chv, Event());

        Assert.Equal(401, reply.Status);
    }

    [Fact]
    public async Task A_token_with_no_worker_in_it_is_refused()
    {
        var reply = await ResultReader.RunAsync(await PostAuditEvents.Handle(
            new AuditUploadRequest([Event()]), new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), null, Sessions, Time,
            NullLogger<PostAuditEvents>.Instance, CancellationToken.None));

        Assert.Equal(401, reply.Status);
    }

    [Fact]
    public async Task The_auditor_reads_the_log_newest_first_and_can_narrow_it_to_one_patient()
    {
        var patient = Guid.NewGuid();
        await UploadAsync(Chv, Event(patient: patient, action: "view"));
        await UploadAsync(Chv, Event(patient: patient, action: "update"));
        await UploadAsync(Chv, Event(patient: Guid.NewGuid()));

        var one = await ReadAsync(Admin, patient);
        var all = await ReadAsync(Admin);

        Assert.Equal(2, one.Body.GetArrayLength());
        Assert.Equal("write", one.Body[0].GetProperty("action").GetString());
        Assert.Equal("read", one.Body[1].GetProperty("action").GetString());
        Assert.Equal("chv-042", one.Body[0].GetProperty("actorId").GetString());
        Assert.Equal(patient.ToString(), one.Body[0].GetProperty("patientId").GetString());
        Assert.True(all.Body.GetArrayLength() >= 3);
        Assert.True(all.Body[0].GetProperty("id").GetInt32() > all.Body[1].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task The_number_of_entries_returned_is_limited()
    {
        for (var i = 0; i < 3; i++)
        {
            await UploadAsync(Chv, Event());
        }

        var few = await ReadAsync(Admin, limit: 2);
        var capped = await ReadAsync(Admin, limit: 1_000_000);
        var floor = await ReadAsync(Admin, limit: -5);

        Assert.Equal(2, few.Body.GetArrayLength());
        Assert.True(capped.Body.GetArrayLength() <= GetAuditEvents.MaxLimit);
        Assert.Equal(1, floor.Body.GetArrayLength());
    }

    [Fact]
    public async Task Reading_the_log_needs_a_worker_who_is_switched_on_and_a_real_token()
    {
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'auditor-02' RETURNING 1");
        var blank = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        var off = await ReadAsync(Admin);
        var nobody = await ResultReader.RunAsync(await GetAuditEvents.Handle(blank, Sessions, CancellationToken.None));
        var verifyOff = await ResultReader.RunAsync(await VerifyAuditEvents.Handle(As(Admin), Sessions, CancellationToken.None));
        var verifyNobody = await ResultReader.RunAsync(await VerifyAuditEvents.Handle(blank, Sessions, CancellationToken.None));

        Assert.Equal([401, 401, 401, 401], new[] { off.Status, nobody.Status, verifyOff.Status, verifyNobody.Status });
    }

    [Fact]
    public async Task The_chain_checks_out_after_uploads_and_notices_an_entry_changed_behind_the_databases_back()
    {
        await UploadAsync(Chv, Event(), Event());

        var intact = await ResultReader.RunAsync(await VerifyAuditEvents.Handle(As(Admin), Sessions, CancellationToken.None));
        Assert.Equal(200, intact.Status);
        Assert.True(intact.Body.GetProperty("intact").GetBoolean());
        Assert.True(intact.Body.GetProperty("entriesChecked").GetInt64() >= 2);

        // Someone with the database owner's rights switches the guard off and rewrites history...
        var victim = await ScalarAsync<int>("SELECT max(id) FROM access_log");
        var original = await ScalarAsync<string>("SELECT accessed_by FROM access_log WHERE id = @victim", new { victim });
        await ScalarAsync<int>("ALTER TABLE access_log DISABLE TRIGGER access_log_no_update_delete; UPDATE access_log SET accessed_by = 'someone-else' WHERE id = @victim; SELECT 1", new { victim });
        try
        {
            var tampered = await ResultReader.RunAsync(await VerifyAuditEvents.Handle(As(Admin), Sessions, CancellationToken.None));

            // ...and the check finds the exact entry.
            Assert.False(tampered.Body.GetProperty("intact").GetBoolean());
            Assert.Equal(victim, tampered.Body.GetProperty("firstBadId").GetInt32());
        }
        finally
        {
            await ScalarAsync<int>("UPDATE access_log SET accessed_by = @original WHERE id = @victim; ALTER TABLE access_log ENABLE TRIGGER access_log_no_update_delete; SELECT 1", new { original, victim });
        }

        var repaired = await ResultReader.RunAsync(await VerifyAuditEvents.Handle(As(Admin), Sessions, CancellationToken.None));
        Assert.True(repaired.Body.GetProperty("intact").GetBoolean());
    }

    [Fact]
    public async Task Entries_written_at_the_same_moment_still_form_one_unbroken_chain()
    {
        // Many phones syncing together: the chain must follow the order entries are committed, not the order their numbers were drawn.
        await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => UploadAsync(Chv, Event())));

        var check = await ResultReader.RunAsync(await VerifyAuditEvents.Handle(As(Admin), Sessions, CancellationToken.None));

        Assert.True(check.Body.GetProperty("intact").GetBoolean(), check.Body.GetRawText());
    }

    [Fact]
    public async Task The_log_cannot_be_edited_or_emptied_by_the_service_itself()
    {
        var id = Guid.NewGuid();
        await UploadAsync(Chv, Event(id));

        var edit = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => ScalarAsync<int>("UPDATE access_log SET accessed_by = 'x' WHERE client_event_id = @id RETURNING 1", new { id }));
        var wipe = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => ScalarAsync<int>("DELETE FROM access_log WHERE client_event_id = @id RETURNING 1", new { id }));

        Assert.Contains("append-only", edit.MessageText);
        Assert.Contains("append-only", wipe.MessageText);
    }
}
