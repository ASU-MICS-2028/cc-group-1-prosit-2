using System.Security.Cryptography;
using System.Text;
using HealthConnect.RecordsService.Calls;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Payments;
using HealthConnect.RecordsService.Tests.Support;
using HealthConnect.SharedLibrary.Security;
using HealthConnect.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthConnect.RecordsService.Tests;

/// <summary>Paystack as the tests want it: what it was asked to start, and what it says when asked about a reference.</summary>
internal sealed class FakePaystack : IPaystack
{
    public List<(string Reference, int Amount, string Callback)> Started { get; } = [];

    public Dictionary<string, PaystackTransaction> Answers { get; } = [];

    public bool Down { get; set; }

    public Task<PaystackCheckout> StartAsync(string reference, int amountPesewas, string email, string callbackUrl, Guid appointmentId, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new PaystackException("down");
        }

        Started.Add((reference, amountPesewas, callbackUrl));
        return Task.FromResult(new PaystackCheckout($"https://checkout.paystack.com/{reference}", "access"));
    }

    public Task<PaystackTransaction?> CheckAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(Answers.TryGetValue(reference, out var answer) ? answer : null);

    public void Pays(string reference, int amount = 2000, string currency = "GHS", string status = "success") =>
        Answers[reference] = new PaystackTransaction(reference, status, amount, currency, "mobile_money", 42, new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
}

[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class PaymentTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly PaymentSettings On = new("sk_test_secret", 2000);
    private readonly FakePaystack _paystack = new();

    private static string Callback(Guid id) => $"https://dwaho59wvxpxe.cloudfront.net/appointments/{id}/payment";

    private async Task<Guid> BookedAsync(string type = "teleconsult", string priority = "urgent")
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        Assert.Equal(201, (await PutAppointmentAsync(id, Appointment(id, patient, type, priority: priority), RecordsDatabase.Chv)).Status);
        return id;
    }

    private async Task<Reply> StatusAsync(Guid id, Worker? who = null, PaymentSettings? settings = null) =>
        await ResultReader.RunAsync(await GetPayment.Handle(
            id, As(who ?? RecordsDatabase.Chv), Sessions, Patients, Payments, settings ?? On, _paystack, NullLogger<GetPayment>.Instance, CancellationToken.None));

    private async Task<Reply> CheckoutAsync(Guid id, string? callback = null, PaymentSettings? settings = null) =>
        await ResultReader.RunAsync(await PostCheckout.Handle(
            id, new CheckoutRequest(callback ?? Callback(id)), As(RecordsDatabase.Chv), Sessions, Patients, Payments, settings ?? On, _paystack, CancellationToken.None));

    private async Task<Reply> TicketAsync(Guid id) =>
        await ResultReader.RunAsync(await PostCallTicket.Handle(
            id, As(RecordsDatabase.Doctor), Sessions, new CallTickets(TimeProvider.System), new CallOptions([]), On, Payments, Patients, CancellationToken.None));

    private async Task<string> PaidAsync(Guid id)
    {
        var reference = (await CheckoutAsync(id)).Body.GetProperty("reference").GetString()!;
        _paystack.Pays(reference);
        Assert.Equal("paid", (await StatusAsync(id)).Body.GetProperty("status").GetString());
        return reference;
    }

    [Fact]
    public async Task A_teleconsult_costs_the_fee_an_emergency_is_free_and_other_visits_have_none()
    {
        var routine = await StatusAsync(await BookedAsync());
        Assert.Equal("unpaid", routine.Body.GetProperty("status").GetString());
        Assert.Equal(2000, routine.Body.GetProperty("amountPesewas").GetInt32());

        Assert.Equal("exempt", (await StatusAsync(await BookedAsync(priority: "emergency"))).Body.GetProperty("status").GetString());
        Assert.Equal("not_required", (await StatusAsync(await BookedAsync("follow_up"))).Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Checkout_opens_paystack_for_the_fee_and_comes_back_to_the_visit()
    {
        var id = await BookedAsync();

        var reply = await CheckoutAsync(id);

        Assert.Equal(200, reply.Status);
        var (reference, amount, callback) = Assert.Single(_paystack.Started);
        Assert.Equal(reference, reply.Body.GetProperty("reference").GetString());
        Assert.Equal(2000, amount);
        Assert.Equal(Callback(id), callback);
        Assert.StartsWith("https://checkout.paystack.com/", reply.Body.GetProperty("authorizationUrl").GetString());
        Assert.Equal("pending", (await StatusAsync(id)).Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task It_is_paid_only_when_paystack_itself_says_so_for_the_right_amount()
    {
        var id = await BookedAsync();
        var reference = (await CheckoutAsync(id)).Body.GetProperty("reference").GetString()!;

        _paystack.Pays(reference, amount: 100);   // someone paid GHS 1 instead of 20

        Assert.Equal("failed", (await StatusAsync(id)).Body.GetProperty("status").GetString());
        Assert.False(await Payments.IsPaidAsync(id, CancellationToken.None));

        await PaidAsync(id);   // a fresh checkout, paid properly
        var paid = await StatusAsync(id);
        Assert.Equal("mobile_money", paid.Body.GetProperty("channel").GetString());
        Assert.Equal(409, (await CheckoutAsync(id)).Status);   // never charged twice
    }

    [Fact]
    public async Task The_doctor_cannot_start_the_call_until_the_fee_is_paid_except_in_an_emergency()
    {
        var id = await BookedAsync();
        var refused = await TicketAsync(id);
        Assert.Equal(402, refused.Status);
        Assert.Equal("payment_required", refused.Error);

        await PaidAsync(id);
        Assert.Equal(200, (await TicketAsync(id)).Status);

        Assert.Equal(200, (await TicketAsync(await BookedAsync(priority: "emergency"))).Status);
    }

    [Fact]
    public async Task Paystack_only_ever_sends_people_back_to_this_visits_payment_page()
    {
        var id = await BookedAsync();

        foreach (var bad in new[] { "https://evil.example/appointments/x/payment", $"https://app.example/appointments/{Guid.NewGuid()}/payment", $"http://app.example/appointments/{id}/payment", $"https://app.example/appointments/{id}/payment?next=evil" })
        {
            Assert.Equal(422, (await CheckoutAsync(id, bad)).Status);
        }

        Assert.True(PostCheckout.IsAllowedCallback($"http://localhost:5173/appointments/{id}/payment", id));
        Assert.Empty(_paystack.Started);
    }

    [Fact]
    public async Task Without_paystack_keys_payments_are_off_and_calls_need_no_fee()
    {
        var id = await BookedAsync();

        Assert.Equal(503, (await CheckoutAsync(id, settings: PaymentsOff)).Status);
        var call = await ResultReader.RunAsync(await PostCallTicket.Handle(
            id, As(RecordsDatabase.Doctor), Sessions, new CallTickets(TimeProvider.System), new CallOptions([]), PaymentsOff, Payments, Patients, CancellationToken.None));
        Assert.Equal(200, call.Status);
    }

    [Fact]
    public async Task When_paystack_is_down_the_checkout_says_so_and_nothing_stays_pending()
    {
        var id = await BookedAsync();
        _paystack.Down = true;

        Assert.Equal(502, (await CheckoutAsync(id)).Status);
        Assert.Equal("failed", (await StatusAsync(id)).Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_worker_who_cannot_see_the_visit_cannot_see_or_pay_its_fee()
    {
        var id = await BookedAsync();

        Assert.Equal(404, (await StatusAsync(id, RecordsDatabase.OtherChv)).Status);
    }

    [Fact]
    public async Task A_webhook_counts_only_with_paystacks_signature()
    {
        var id = await BookedAsync();
        var reference = (await CheckoutAsync(id)).Body.GetProperty("reference").GetString()!;
        var body = Encoding.UTF8.GetBytes($$$"""
            {"event":"charge.success","data":{"id":7,"reference":"{{{reference}}}","status":"success","amount":2000,"currency":"GHS","channel":"mobile_money","paid_at":"2026-11-12T09:00:00.000Z"}}
            """);

        Assert.Equal(401, await WebhookAsync(body, "00ff"));
        Assert.False(await Payments.IsPaidAsync(id, CancellationToken.None));

        var signature = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes("sk_test_secret"), body)).ToLowerInvariant();
        Assert.Equal(200, await WebhookAsync(body, signature));
        Assert.True(await Payments.IsPaidAsync(id, CancellationToken.None));
    }

    private async Task<int> WebhookAsync(byte[] body, string signature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);
        context.Request.Headers["x-paystack-signature"] = signature;
        return (await ResultReader.RunAsync(await PaystackWebhook.Handle(
            context.Request, Payments, On, NullLogger<PaystackWebhook>.Instance, CancellationToken.None))).Status;
    }

    [Fact]
    public async Task The_officer_sees_their_districts_fees_without_any_patient()
    {
        var id = await BookedAsync();
        await PaidAsync(id);
        await CheckoutAsync(await BookedAsync());   // another, still pending

        var reply = await ResultReader.RunAsync(await GetPayments.Handle(As(RecordsDatabase.Officer), Payments, CancellationToken.None));

        Assert.Equal(200, reply.Status);
        Assert.Equal(1, reply.Body.GetProperty("paidCount").GetInt32());
        Assert.Equal(2000, reply.Body.GetProperty("paidPesewas").GetInt32());
        Assert.Equal(1, reply.Body.GetProperty("pendingCount").GetInt32());
        var first = reply.Body.GetProperty("items")[0];
        Assert.False(first.TryGetProperty("patientId", out _));
        Assert.Equal("Akosua Mensah", reply.Body.GetProperty("items").EnumerateArray().First(i => i.GetProperty("status").GetString() == "paid").GetProperty("collectedBy").GetString());

        var elsewhere = new Worker("ghs-officer-99", "Other Officer", "district_officer", "Kintampo North");
        var none = await ResultReader.RunAsync(await GetPayments.Handle(As(elsewhere), Payments, CancellationToken.None));
        Assert.Equal(0, none.Body.GetProperty("items").GetArrayLength());
    }
}
