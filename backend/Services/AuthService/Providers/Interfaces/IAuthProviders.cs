using HealthConnect.Data.Entities;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.IdentityModel.Tokens;

namespace HealthConnect.AuthService.Providers.Interfaces;

/// <summary>Makes a random 6-digit sign-in code.</summary>
public interface ICodeGenerator
{
    string NewCode();
}

/// <summary>Turns a code into the text kept in the database, and checks a guess against it, in constant time.</summary>
public interface ICodeHasher
{
    ValueTask<string> HashAsync(PhoneNumber phone, string code, CancellationToken cancellationToken);

    ValueTask<bool> MatchesAsync(PhoneNumber phone, string code, string storedHash, CancellationToken cancellationToken);
}

/// <summary>The secret tokens are signed with.</summary>
public interface ISigningKeyProvider
{
    /// <summary>The key once it has been loaded, else null. Token checking reads this without waiting.</summary>
    SymmetricSecurityKey? Cached { get; }

    /// <summary>Loads the key (from settings, or from the database, making it the first time) and remembers it.</summary>
    ValueTask<SymmetricSecurityKey> GetAsync(CancellationToken cancellationToken);
}

/// <summary>A token and when it stops working.</summary>
public sealed record IssuedToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Makes the signed token a person sends on every call.</summary>
public interface ITokenIssuer
{
    Task<IssuedToken> IssueAsync(AppUser user, CancellationToken cancellationToken);
}
