using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Security;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace HealthConnect.AuthService.Features;

/// <summary>
/// Patients sign in with the phone number a CHV put on their record. There is no separate registration: the first time a code is asked
/// for a number that belongs to no health worker but to one or more patient records (a household often shares one phone), a patient
/// account is made for that number, and it is kept in step with the records each time. A number that no longer appears on any record
/// is switched off. A health worker's number is always the worker's.
/// </summary>
public static class PatientAccounts
{
    public static string IdFor(PhoneNumber phone) => $"patient-{phone.E164.TrimStart('+')}";

    public static async Task<AppUser?> SyncAsync(AppDbContext db, PhoneNumber phone, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Phone == phone.E164 && u.Role != Roles.Patient, cancellationToken))
        {
            return null;
        }

        var people = await db.Database
            .SqlQuery<RecordName>($"SELECT full_name AS \"FullName\", district AS \"District\" FROM patients WHERE phone = {phone.E164} ORDER BY id")
            .ToListAsync(cancellationToken);
        var account = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone.E164 && u.Role == Roles.Patient, cancellationToken);

        if (people.Count == 0)
        {
            if (account is { Active: true })
            {
                account.Active = false;
                await db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        var name = people.Count == 1 ? people[0].FullName : $"{people[0].FullName} (+{people.Count - 1})";
        if (account is null)
        {
            account = new AppUser
            {
                Id = IdFor(phone),
                Phone = phone.E164,
                Role = Roles.Patient,
                CreatedAt = now,
            };
            db.Users.Add(account);
        }

        account.Name = name;
        account.District = people[0].District;
        account.Active = true;
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    private sealed class RecordName
    {
        public string FullName { get; set; } = "";

        public string District { get; set; } = "";
    }
}
