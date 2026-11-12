using Dapper;
using HealthConnect.Data;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HealthConnect.RecordsService.Payments;

/// <summary>One payment as stored (the payments table, migration 7).</summary>
public sealed class PaymentRow
{
    public string Reference { get; set; } = "";

    public Guid AppointmentId { get; set; }

    public string District { get; set; } = "";

    public int AmountPesewas { get; set; }

    public string Currency { get; set; } = "GHS";

    public string Status { get; set; } = "pending";

    public string? Channel { get; set; }

    public long? ProviderId { get; set; }

    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }
}

/// <summary>A payment in the district list, with what the officer may see of the visit (never the patient).</summary>
public sealed class PaymentListRow
{
    public string Reference { get; set; } = "";

    public Guid AppointmentId { get; set; }

    public string District { get; set; } = "";

    public int AmountPesewas { get; set; }

    public string Status { get; set; } = "";

    public string? Channel { get; set; }

    public string CreatedBy { get; set; } = "";

    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public string Facility { get; set; } = "";

    public DateTime ScheduledAt { get; set; }
}

/// <summary>
/// The payments table, read and written with the API's own login: no per-role database login has a grant on it. Every caller first
/// proves through its own role session that it may see the appointment (row-level security), so this never widens what anyone sees.
/// </summary>
public sealed class PaymentStore(Database database, ILogger<PaymentStore> logger)
{
    private const string Columns =
        "reference, appointment_id, district, amount_pesewas, currency, status, channel, provider_id, created_by, created_at, paid_at";

    public async Task<PaymentRow?> LatestAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            $"SELECT {Columns} FROM payments WHERE appointment_id = @appointmentId ORDER BY (status = 'paid') DESC, created_at DESC LIMIT 1",
            new { appointmentId }, cancellationToken: cancellationToken));
    }

    public async Task<PaymentRow?> FindAsync(string reference, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            $"SELECT {Columns} FROM payments WHERE reference = @reference", new { reference }, cancellationToken: cancellationToken));
    }

    public async Task<bool> IsPaidAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM payments WHERE appointment_id = @appointmentId AND status = 'paid')",
            new { appointmentId }, cancellationToken: cancellationToken));
    }

    /// <summary>Starts a payment for a visit; the district is the patient's, read here so the caller can't choose it.</summary>
    public async Task<PaymentRow> StartAsync(string reference, Guid appointmentId, int amountPesewas, string createdBy, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<PaymentRow>(new CommandDefinition(
            $"""
            INSERT INTO payments (reference, appointment_id, district, amount_pesewas, created_by)
            SELECT @reference, a.id, p.district, @amountPesewas, @createdBy
              FROM appointments a JOIN patients p ON p.id = a.patient_id
             WHERE a.id = @appointmentId
            RETURNING {Columns}
            """,
            new { reference, appointmentId, amountPesewas, createdBy }, cancellationToken: cancellationToken));
    }

    public async Task MarkAsync(string reference, string status, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE payments SET status = @status, updated_at = now() WHERE reference = @reference AND status = 'pending'",
            new { reference, status }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// What Paystack says happened. A payment only becomes paid when Paystack reports success for exactly the amount and currency we
    /// asked for; a paid payment never changes again. Returns the row as it is afterwards.
    /// </summary>
    public async Task<PaymentRow?> ApplyAsync(PaystackTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await FindAsync(transaction.Reference, cancellationToken);
        if (row is null || row.Status == "paid")
        {
            return row;
        }

        var status = transaction.Status switch
        {
            "success" when transaction.Amount == row.AmountPesewas && transaction.Currency == row.Currency => "paid",
            "success" => "failed", // paid, but not what we asked for: never count it
            "failed" or "reversed" => "failed",
            "abandoned" => "abandoned",
            _ => "pending",
        };
        if (status == "failed" && transaction.Status == "success")
        {
            logger.LogWarning(
                "Payment {Reference} succeeded for {Amount} {Currency}, but {Expected} GHS was asked; not counted",
                row.Reference, transaction.Amount, transaction.Currency, row.AmountPesewas);
        }

        if (status == row.Status)
        {
            return row;
        }

        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE payments
                   SET status = @status, channel = @channel, provider_id = @providerId,
                       paid_at = CASE WHEN @status = 'paid' THEN coalesce(@paidAt::timestamptz, now()) END, updated_at = now()
                 WHERE reference = @reference AND status <> 'paid'
                """,
                new { status, channel = transaction.Channel, providerId = transaction.Id, paidAt = transaction.PaidAt?.UtcDateTime, reference = row.Reference },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException conflict) when (conflict.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The visit was already paid through another checkout: this second payment needs a refund by hand.
            logger.LogWarning("Payment {Reference} succeeded but visit {Appointment} was already paid; refund it in Paystack", row.Reference, row.AppointmentId);
            return row;
        }

        return await FindAsync(row.Reference, cancellationToken);
    }

    /// <summary>The newest payments, for one district (the officer's) or all (the auditor), with the visit and who collected it.</summary>
    public async Task<List<PaymentListRow>> ListAsync(string? district, CancellationToken cancellationToken)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<PaymentListRow>(new CommandDefinition(
            """
            SELECT pay.reference, pay.appointment_id, pay.district, pay.amount_pesewas, pay.status, pay.channel, pay.created_by,
                   u.name AS created_by_name, pay.created_at, pay.paid_at, a.facility, a.scheduled_at
              FROM payments pay
              JOIN appointments a ON a.id = pay.appointment_id
              LEFT JOIN users u ON u.id = pay.created_by
             WHERE @district::text IS NULL OR pay.district = @district
             ORDER BY pay.created_at DESC
             LIMIT 200
            """,
            new { district }, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
