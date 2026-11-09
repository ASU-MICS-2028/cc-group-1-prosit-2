using System.Security.Claims;

namespace HealthConnect.SharedLibrary.Security;

/// <summary>The names of the claims in the sign-in token (short names, as the token carries them).</summary>
public static class AuthClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string District = "district";
}

/// <summary>
/// Who is calling. It always comes from the signed token, never from the request body (the contract's rule: a person cannot
/// claim to be someone else, least of all in the audit trail).
/// </summary>
public sealed record CurrentUser(string Id, string Name, string Role, string District)
{
    /// <summary>The caller, or null when the token lacks what a person must have (an old or foreign token).</summary>
    public static CurrentUser? From(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(AuthClaims.UserId);
        var role = principal.FindFirstValue(AuthClaims.Role);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return new CurrentUser(
            id,
            principal.FindFirstValue(AuthClaims.Name) ?? string.Empty,
            role,
            principal.FindFirstValue(AuthClaims.District) ?? string.Empty);
    }
}
