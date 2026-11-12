namespace HealthConnect.SharedLibrary.Security;

/// <summary>
/// The kinds of signed-in person, spelled exactly as the app and the users table spell them.
/// The role matrix (who may do what) lives in <see cref="AuthPolicies"/> and, for patient data, in the database roles.
/// </summary>
public static class Roles
{
    public const string Chv = "chv";
    public const string Nurse = "nurse";
    public const string Doctor = "doctor";
    public const string DistrictOfficer = "district_officer";

    /// <summary>The auditor: reads the activity log and the district summary, never a patient.</summary>
    public const string Admin = "admin";

    /// <summary>
    /// A patient (or a household sharing one phone): signs in with the number on their record, sees only those records, pays their
    /// own fees and joins their own teleconsult. Never one of the workers' database roles.
    /// </summary>
    public const string Patient = "patient";

    /// <summary>The health workers: everyone who works in the system, as opposed to the patients it serves.</summary>
    public static readonly IReadOnlyList<string> Workers = [Chv, Nurse, Doctor, DistrictOfficer, Admin];

    public static readonly IReadOnlyList<string> All = [.. Workers, Patient];
}
