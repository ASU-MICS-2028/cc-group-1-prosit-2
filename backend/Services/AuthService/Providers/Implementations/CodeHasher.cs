using System.Security.Cryptography;
using System.Text;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.SharedLibrary.ValueObjects;

namespace HealthConnect.AuthService.Providers.Implementations;

/// <summary>
/// HMAC-SHA256 of the number and the code, keyed with the token signing key: 64 hex characters. The number is part of what
/// is hashed, so the same code for two numbers gives two different hashes, and without the key a leaked hash cannot be
/// tried against the million possible codes. A guess is compared in constant time.
/// </summary>
public sealed class CodeHasher(ISigningKeyProvider keys) : ICodeHasher
{
    public async ValueTask<string> HashAsync(PhoneNumber phone, string code, CancellationToken cancellationToken)
    {
        var key = await keys.GetAsync(cancellationToken);
        var data = Encoding.UTF8.GetBytes($"{phone.E164}|{code}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(key.Key, data));
    }

    public async ValueTask<bool> MatchesAsync(PhoneNumber phone, string code, string storedHash, CancellationToken cancellationToken)
    {
        var expected = Encoding.ASCII.GetBytes(await HashAsync(phone, code, cancellationToken));
        var stored = Encoding.ASCII.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(expected, stored);
    }
}
