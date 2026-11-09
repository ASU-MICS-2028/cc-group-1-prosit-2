using System.Text.RegularExpressions;
using HealthConnect.RecordsService.Models;
using HealthConnect.SharedLibrary.ValueObjects;

namespace HealthConnect.RecordsService.Validation;

/// <summary>The values the app and the database agree on.</summary>
public static class Vocabulary
{
    public static readonly string[] Sexes = ["female", "male", "other"];
    public static readonly string[] Languages = ["en", "tw", "ee", "dag", "ha"];
    public static readonly string[] ConsentMethods = ["spoken", "thumbprint", "signature"];
    public static readonly string[] AppointmentTypes = ["follow_up", "antenatal", "postnatal", "referral", "teleconsult"];
    public static readonly string[] Priorities = ["routine", "urgent", "emergency"];
    public static readonly string[] AppointmentStatuses = ["scheduled", "completed", "missed", "cancelled"];
    public static readonly string[] ReferralStatuses = ["requested", "accepted", "declined", "seen"];

    /// <summary>Referrals and teleconsults go through the requested, accepted or declined, seen steps.</summary>
    public static bool HasReferralStatus(string type) => type is "referral" or "teleconsult";
}

public static partial class PatientValidator
{
    /// <summary>Checks a patient the phone sent. Returns the wrong fields (empty = fine) and the number cleaned up to +233 form.</summary>
    public static (Dictionary<string, string> Errors, string? Phone) Check(Guid pathId, PatientDto patient, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();

        if (!Guid.TryParse(patient.Id, out var bodyId) || bodyId != pathId)
        {
            errors["id"] = "The id in the record must be the id in the address.";
        }

        Required(errors, "givenName", patient.GivenName, 100);
        Required(errors, "familyName", patient.FamilyName, 100);
        Required(errors, "community", patient.Community, 100);
        Required(errors, "district", patient.District, 100);

        if (!Vocabulary.Sexes.Contains(patient.Sex))
        {
            errors["sex"] = "Choose female, male or other.";
        }

        if (!Vocabulary.Languages.Contains(patient.PreferredLanguage))
        {
            errors["preferredLanguage"] = "Choose English, Twi, Ewe, Dagbani or Hausa.";
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (patient.DateOfBirth > today || patient.DateOfBirth.Year < 1900)
        {
            errors["dateOfBirth"] = "Enter a birth date that is not in the future.";
        }

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(patient.Phone))
        {
            if (PhoneNumber.TryParse(patient.Phone, out var parsed))
            {
                phone = parsed.E164;
            }
            else
            {
                errors["phone"] = "Enter a Ghana mobile number.";
            }
        }

        if (patient.NationalId is { Length: > 40 })
        {
            errors["nationalId"] = "Too long.";
        }

        if (patient.Version < 0)
        {
            errors["version"] = "The version cannot be negative.";
        }

        CheckConsent(errors, patient.Consent, now);
        return (errors, phone);
    }

    private static void CheckConsent(Dictionary<string, string> errors, ConsentDto? consent, DateTimeOffset now)
    {
        if (consent is null || !consent.Given)
        {
            errors["consent"] = "Nothing can be saved without the patient's consent.";
            return;
        }

        if (!Vocabulary.ConsentMethods.Contains(consent.Method))
        {
            errors["consent.method"] = "Choose spoken, thumbprint or signature.";
        }

        if (consent.At > now.AddDays(1) || consent.At.Year < 2020)
        {
            errors["consent.at"] = "Enter when consent was given.";
        }

        if (string.IsNullOrWhiteSpace(consent.StatementVersion) || consent.StatementVersion.Length > 20)
        {
            errors["consent.statementVersion"] = "Say which wording was read to the patient.";
        }

        if (consent.Language is not null && !Vocabulary.Languages.Contains(consent.Language))
        {
            errors["consent.language"] = "Unknown language.";
        }

        if (consent.Evidence is null && string.IsNullOrWhiteSpace(consent.Witness))
        {
            errors["consent"] = "Add the recording, signature or photo, or name a witness.";
        }

        if (consent.Evidence is { } evidence)
        {
            if (!Guid.TryParse(evidence.MediaId, out _))
            {
                errors["consent.evidence.mediaId"] = "Not a valid id.";
            }

            if (!Sha256().IsMatch(evidence.Sha256 ?? string.Empty))
            {
                errors["consent.evidence.sha256"] = "Not a SHA-256 hash (64 lower-case hex characters).";
            }

            if (evidence.Mime is null || !(evidence.Mime.StartsWith("audio/", StringComparison.Ordinal) || evidence.Mime is "image/png" or "image/jpeg"))
            {
                errors["consent.evidence.mime"] = "Only audio, PNG and JPEG are accepted.";
            }
        }
    }

    private static void Required(Dictionary<string, string> errors, string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = "Required.";
        }
        else if (value.Length > max)
        {
            errors[field] = $"At most {max} characters.";
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}

public static class VitalsValidator
{
    /// <summary>The plausible range of each measurement (the database holds the same ranges in observation_codes and enforces them again).</summary>
    public sealed record Range(string Field, string Type, decimal Min, decimal Max, string Unit);

    public static readonly IReadOnlyList<Range> Ranges =
    [
        new("systolic", "systolic_bp", 50, 260, "mmHg"),
        new("diastolic", "diastolic_bp", 30, 160, "mmHg"),
        new("temperatureC", "body_temperature", 30, 45, "°C"),
        new("pulse", "heart_rate", 20, 250, "per minute"),
        new("weightKg", "body_weight", 0.5m, 300, "kg"),
        new("heightCm", "body_height", 30, 250, "cm"),
    ];

    public static Dictionary<string, string> Check(Guid pathId, VitalsDto vitals, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();

        if (!Guid.TryParse(vitals.Id, out var bodyId) || bodyId != pathId)
        {
            errors["id"] = "The id in the record must be the id in the address.";
        }

        if (!Guid.TryParse(vitals.PatientId, out _))
        {
            errors["patientId"] = "Not a valid patient id.";
        }

        if (vitals.TakenAt > now.AddDays(1) || vitals.TakenAt.Year < 2020)
        {
            errors["takenAt"] = "Enter when the measurement was taken.";
        }

        var values = Values(vitals);
        if (values.All(v => v.Value is null))
        {
            errors["vitals"] = "Record at least one measurement.";
        }

        if ((vitals.Systolic is null) != (vitals.Diastolic is null))
        {
            errors[vitals.Systolic is null ? "systolic" : "diastolic"] = "Blood pressure needs both numbers.";
        }

        foreach (var (range, value) in Ranges.Zip(values.Select(v => v.Value)))
        {
            if (value is { } number && (number < range.Min || number > range.Max))
            {
                errors[range.Field] = $"Enter a value between {range.Min} and {range.Max} {range.Unit}.";
            }
        }

        if (vitals.Systolic is { } sys && vitals.Diastolic is { } dia && sys <= dia && !errors.ContainsKey("systolic") && !errors.ContainsKey("diastolic"))
        {
            errors["systolic"] = "The top number must be higher than the bottom number.";
        }

        if (vitals.Notes is { Length: > 2000 })
        {
            errors["notes"] = "At most 2000 characters.";
        }

        if (vitals.Version < 0)
        {
            errors["version"] = "The version cannot be negative.";
        }

        return errors;
    }

    /// <summary>The measurements in the same order as <see cref="Ranges"/>.</summary>
    public static IReadOnlyList<(Range Range, decimal? Value)> Values(VitalsDto vitals) =>
    [
        (Ranges[0], vitals.Systolic),
        (Ranges[1], vitals.Diastolic),
        (Ranges[2], vitals.TemperatureC),
        (Ranges[3], vitals.Pulse),
        (Ranges[4], vitals.WeightKg),
        (Ranges[5], vitals.HeightCm),
    ];
}

public static class AppointmentValidator
{
    public static Dictionary<string, string> Check(Guid pathId, AppointmentDto appointment, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();

        if (!Guid.TryParse(appointment.Id, out var bodyId) || bodyId != pathId)
        {
            errors["id"] = "The id in the record must be the id in the address.";
        }

        if (!Guid.TryParse(appointment.PatientId, out _))
        {
            errors["patientId"] = "Not a valid patient id.";
        }

        if (!Vocabulary.AppointmentTypes.Contains(appointment.Type))
        {
            errors["type"] = "Choose follow_up, antenatal, postnatal, referral or teleconsult.";
        }

        if (!Vocabulary.Priorities.Contains(appointment.Priority))
        {
            errors["priority"] = "Choose routine, urgent or emergency.";
        }

        if (!Vocabulary.AppointmentStatuses.Contains(appointment.Status))
        {
            errors["status"] = "Choose scheduled, completed, missed or cancelled.";
        }

        if (string.IsNullOrWhiteSpace(appointment.Facility) || appointment.Facility.Length > 200)
        {
            errors["facility"] = "Enter the health facility (up to 200 characters).";
        }

        if (appointment.Reason is { Length: > 2000 })
        {
            errors["reason"] = "At most 2000 characters.";
        }

        if (appointment.ScheduledAt.Year < 2020 || appointment.ScheduledAt > now.AddYears(2))
        {
            errors["scheduledAt"] = "Enter a date and time for the visit.";
        }

        if (appointment.ReferralStatus is not null)
        {
            if (!Vocabulary.ReferralStatuses.Contains(appointment.ReferralStatus))
            {
                errors["referralStatus"] = "Choose requested, accepted, declined or seen.";
            }
            else if (!Vocabulary.HasReferralStatus(appointment.Type))
            {
                errors["referralStatus"] = "Only a referral or a teleconsult has a referral status.";
            }
        }

        if (appointment.Version < 0)
        {
            errors["version"] = "The version cannot be negative.";
        }

        return errors;
    }

    /// <summary>
    /// Where a referral may go next: requested to accepted or declined, accepted to seen. Staying put is fine; going backwards or
    /// skipping a step is not, because two workers can change the same referral from two phones.
    /// </summary>
    public static bool MayMove(string? from, string to) =>
        from == to || (from, to) switch
        {
            ("requested", "accepted") or ("requested", "declined") or ("accepted", "seen") => true,
            _ => false,
        };
}
