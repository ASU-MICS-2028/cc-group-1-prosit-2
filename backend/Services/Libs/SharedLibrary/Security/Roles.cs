namespace HealthConnect.SharedLibrary.Security;

/// <summary>
/// The five kinds of signed-in person, spelled exactly as the app and the users table spell them.
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

    public static readonly IReadOnlyList<string> All = [Chv, Nurse, Doctor, DistrictOfficer, Admin];
}
