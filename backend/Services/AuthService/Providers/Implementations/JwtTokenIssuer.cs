using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.Data.Entities;
using HealthConnect.SharedLibrary.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HealthConnect.AuthService.Providers.Implementations;

/// <summary>
/// Makes the sign-in token: a JWT signed with HMAC-SHA256 that says who the person is (id, name, role, district) and when it
/// stops working. The person's role and district come from here on every later call, never from what a phone says.
/// </summary>
public sealed class JwtTokenIssuer(ISigningKeyProvider keys, IOptions<AuthOptions> options, TimeProvider time) : ITokenIssuer
{
    public async Task<IssuedToken> IssueAsync(AppUser user, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var expires = now + settings.TokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(await keys.GetAsync(cancellationToken), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AuthClaims.UserId] = user.Id,
                [AuthClaims.Name] = user.Name,
                [AuthClaims.Role] = user.Role,
                [AuthClaims.District] = user.District,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        };

        return new IssuedToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
