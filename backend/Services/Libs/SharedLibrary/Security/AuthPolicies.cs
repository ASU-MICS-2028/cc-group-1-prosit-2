using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.SharedLibrary.Security;

/// <summary>
/// The access rules an endpoint names with RequireAuthorization(AuthPolicies.X). They mirror the app's own page rules
/// (frontend/src/app/router.tsx), because hiding a page is not access control: the API enforces them too.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Anyone signed in.</summary>
    public const string AnyWorker = "AnyWorker";

    /// <summary>Anyone signed in, worker or patient: their own account (who am I, my photo).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>A patient, for their own records.</summary>
    public const string Patient = "Patient";

    /// <summary>A visit's call and fee: the clinical workers, or the patient whose visit it is (checked in the handler).</summary>
    public const string ClinicalOrPatient = "ClinicalOrPatient";

    /// <summary>Adding and switching off health workers: the district officer (their district) and the admin (every district).</summary>
    public const string ManageWorkers = "ManageWorkers";

    /// <summary>Works with patients: CHV, nurse, doctor.</summary>
    public const string Clinical = "Clinical";

    /// <summary>Handles referrals: nurse, doctor.</summary>
    public const string Clinician = "Clinician";

    /// <summary>The anonymised district summary: district officer, auditor.</summary>
    public const string Summary = "Summary";

    /// <summary>The activity log: the auditor only.</summary>
    public const string Auditor = "Auditor";

    public static IServiceCollection AddHealthConnectPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AnyWorker, policy => policy.RequireRole(Roles.Workers))
            .AddPolicy(SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Patient, policy => policy.RequireRole(Roles.Patient))
            .AddPolicy(ClinicalOrPatient, policy => policy.RequireRole(Roles.Chv, Roles.Nurse, Roles.Doctor, Roles.Patient))
            .AddPolicy(ManageWorkers, policy => policy.RequireRole(Roles.DistrictOfficer, Roles.Admin))
            .AddPolicy(Clinical, policy => policy.RequireRole(Roles.Chv, Roles.Nurse, Roles.Doctor))
            .AddPolicy(Clinician, policy => policy.RequireRole(Roles.Nurse, Roles.Doctor))
            .AddPolicy(Summary, policy => policy.RequireRole(Roles.DistrictOfficer, Roles.Admin))
            .AddPolicy(Auditor, policy => policy.RequireRole(Roles.Admin));
        return services;
    }
}
