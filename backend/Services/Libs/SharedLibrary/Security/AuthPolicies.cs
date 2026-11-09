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
            .AddPolicy(AnyWorker, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Clinical, policy => policy.RequireRole(Roles.Chv, Roles.Nurse, Roles.Doctor))
            .AddPolicy(Clinician, policy => policy.RequireRole(Roles.Nurse, Roles.Doctor))
            .AddPolicy(Summary, policy => policy.RequireRole(Roles.DistrictOfficer, Roles.Admin))
            .AddPolicy(Auditor, policy => policy.RequireRole(Roles.Admin));
        return services;
    }
}
