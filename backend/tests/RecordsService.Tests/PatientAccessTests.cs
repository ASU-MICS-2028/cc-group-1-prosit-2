using Dapper;
using HealthConnect.RecordsService.Calls;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Payments;
using HealthConnect.RecordsService.Tests.Support;
using HealthConnect.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.RecordsService.Tests;

/// <summary>A signed-in patient: their own records, their own call and fee, and nothing of anyone else's.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class PatientAccessTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private const string Phone = "+233598256428";
    private static readonly Worker Abena = new("patient-233598256428", "Abena Owusu", "patient", "Shai-Osudoku");
    private readonly FakePaystack _paystack = new();

    /// <summary>A record registered by the CHV, with or without the patient's phone, and the patient account for that phone.</summary>
    private async Task<Guid> RecordAsync(string? phone, string given = "Abena")
    {
        var id = Guid.NewGuid();
        Assert.Equal(201, (await PutPatientAsync(id, Patient(id, given: given) with { Phone = phone }, RecordsDatabase.Chv)).Status);
        await using var connection = await Database.Owner.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO users (id, phone, name, role, district) VALUES (@Id, @Phone, @Name, 'patient', @District) ON CONFLICT DO NOTHING",
            new { Abena.Id, Phone, Abena.Name, Abena.District });
        return id;
    }

    private async Task<Guid> TeleconsultAsync(Guid patient, string priority = "urgent")
    {
        var id = Guid.NewGuid();
        Assert.Equal(201, (await PutAppointmentAsync(id, Appointment(id, patient, "teleconsult", priority: priority), RecordsDatabase.Chv)).Status);
        return id;
    }

    private async Task<Reply> TicketAsync(Guid id, Worker who, PaymentSettings? settings = null) =>
        await ResultReader.RunAsync(await PostCallTicket.Handle(
            id, As(who), Sessions, new CallTickets(TimeProvider.System), new CallOptions([]), settings ?? PaymentsOff, Payments, Patients, CancellationToken.None));

    [Fact]
    public async Task A_patient_sees_everyone_registered_with_their_phone_and_no_one_else()
    {
        var abena = await RecordAsync(Phone);
        var kojo = await RecordAsync(Phone, "Kojo");
        var stranger = await RecordAsync("+233241112222", "Esi");
        var visit = await TeleconsultAsync(abena);
        await TeleconsultAsync(stranger);

        var reply = await ResultReader.RunAsync(await GetMyHealth.Handle(As(Abena), Patients, CancellationToken.None));

        Assert.Equal(200, reply.Status);
        var people = reply.Body.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Equal([abena.ToString(), kojo.ToString()], people);
        var visits = reply.Body.GetProperty("appointments").EnumerateArray().Select(a => a.GetProperty("id").GetString()).ToList();
        Assert.Equal([visit.ToString()], visits);
    }

    [Fact]
    public async Task A_worker_token_gets_nothing_from_the_patient_endpoint()
    {
        var reply = await ResultReader.RunAsync(await GetMyHealth.Handle(As(RecordsDatabase.Chv), Patients, CancellationToken.None));

        Assert.Equal(401, reply.Status);
    }

    [Fact]
    public async Task A_patient_can_join_their_own_teleconsult_but_not_anyone_elses()
    {
        var mine = await TeleconsultAsync(await RecordAsync(Phone));
        var theirs = await TeleconsultAsync(await RecordAsync("+233241112222", "Esi"));

        Assert.Equal(200, (await TicketAsync(mine, Abena)).Status);
        Assert.Equal(404, (await TicketAsync(theirs, Abena)).Status);
    }

    [Fact]
    public async Task A_patient_pays_their_own_fee_and_the_officer_never_sees_who_they_are()
    {
        var visit = await TeleconsultAsync(await RecordAsync(Phone));
        var on = new PaymentSettings("sk_test_secret", 2000);
        Assert.Equal(402, (await TicketAsync(visit, Abena, on)).Status);

        var checkout = await ResultReader.RunAsync(await PostCheckout.Handle(
            visit, new CheckoutRequest($"https://app.example/appointments/{visit}/payment"), As(Abena), Sessions, Patients, Payments, on, _paystack,
            CancellationToken.None));
        Assert.Equal(200, checkout.Status);
        _paystack.Pays(checkout.Body.GetProperty("reference").GetString()!);
        var status = await ResultReader.RunAsync(await GetPayment.Handle(
            visit, As(Abena), Sessions, Patients, Payments, on, _paystack, NullLogger<GetPayment>.Instance, CancellationToken.None));
        Assert.Equal("paid", status.Body.GetProperty("status").GetString());
        Assert.Equal(200, (await TicketAsync(visit, Abena, on)).Status);

        var list = await ResultReader.RunAsync(await GetPayments.Handle(As(RecordsDatabase.Officer), Payments, CancellationToken.None));
        var item = list.Body.GetProperty("items")[0];
        Assert.Equal("patient", item.GetProperty("collectedBy").GetString());
        Assert.DoesNotContain("233598256428", list.Body.GetRawText());
        Assert.DoesNotContain("Abena", list.Body.GetRawText());
    }

    [Fact]
    public async Task A_switched_off_patient_account_reads_nothing()
    {
        await RecordAsync(Phone);
        await using (var connection = await Database.Owner.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE users SET active = false WHERE role = 'patient'");
        }

        Assert.Equal(401, (await ResultReader.RunAsync(await GetMyHealth.Handle(As(Abena), Patients, CancellationToken.None))).Status);
    }
}
