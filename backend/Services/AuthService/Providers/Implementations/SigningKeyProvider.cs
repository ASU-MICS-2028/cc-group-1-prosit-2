using System.Security.Cryptography;
using System.Text;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HealthConnect.AuthService.Providers.Implementations;

/// <summary>
/// Finds the key tokens are signed with. If Auth:SigningKey is set (a secret from AWS, say), that is used. Otherwise the key is
/// 32 random bytes kept in the database (auth_keys): the first API to start makes it, and every other container and every
/// restart reads the same one, so no new secret has to be set up in AWS. Only the API's own database login can read that table.
/// A failure to load the key is never remembered: the next call tries again, so a short database outage heals itself.
/// </summary>
public sealed class SigningKeyProvider(Database database, IOptions<AuthOptions> options) : ISigningKeyProvider
{
    public const int MinimumKeyBytes = 32;

    private readonly SemaphoreSlim _loading = new(1, 1);
    private volatile SymmetricSecurityKey? _key;

    public SymmetricSecurityKey? Cached => _key;

    public async ValueTask<SymmetricSecurityKey> GetAsync(CancellationToken cancellationToken)
    {
        if (_key is { } known)
        {
            return known;
        }

        await _loading.WaitAsync(cancellationToken);
        try
        {
            _key ??= new SymmetricSecurityKey(await LoadAsync(cancellationToken));
            return _key;
        }
        finally
        {
            _loading.Release();
        }
    }

    private async Task<byte[]> LoadAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value.SigningKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var bytes = Encoding.UTF8.GetBytes(configured);
            return bytes.Length >= MinimumKeyBytes
                ? bytes
                : throw new InvalidOperationException($"Auth:SigningKey must be at least {MinimumKeyBytes} bytes long.");
        }

        // Make the key if nobody has yet (two APIs starting together: one insert wins, both then read the same row).
        await using (var insert = database.DataSource.CreateCommand(
            "INSERT INTO auth_keys (signing_key) VALUES (@key) ON CONFLICT (id) DO NOTHING"))
        {
            insert.Parameters.AddWithValue("key", RandomNumberGenerator.GetBytes(MinimumKeyBytes));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = database.DataSource.CreateCommand("SELECT signing_key FROM auth_keys WHERE id = 1");
        return (byte[]?)await read.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The token signing key could not be read.");
    }
}
