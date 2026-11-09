using System.Text.RegularExpressions;

namespace HealthConnect.SharedLibrary.ValueObjects;

/// <summary>
/// A Ghana mobile number in international form (+233 and 9 digits), the only form stored. It accepts what a person types
/// (0241234567, 241234567, 233241234567, +233 24 123 4567), the same ways the app does (frontend/src/api/auth.ts).
/// </summary>
public readonly partial record struct PhoneNumber
{
    private PhoneNumber(string e164) => E164 = e164;

    /// <summary>The number as stored, e.g. +233241234567.</summary>
    public string E164 { get; }

    /// <summary>Safe to log: +233 24 *** **67. A phone number is personal data, so a log never carries the whole number.</summary>
    public string Masked => $"{E164[..7]} *** **{E164[^2..]}";

    public static bool TryParse(string? input, out PhoneNumber phone)
    {
        phone = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = Separators().Replace(input, string.Empty);
        var match = Ghana().Match(digits);
        if (!match.Success)
        {
            return false;
        }

        phone = new PhoneNumber($"+233{match.Groups[1].Value}");
        return true;
    }

    public override string ToString() => Masked;

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^(?:\+233|233|0)?([2-9]\d{8})$")]
    private static partial Regex Ghana();
}
