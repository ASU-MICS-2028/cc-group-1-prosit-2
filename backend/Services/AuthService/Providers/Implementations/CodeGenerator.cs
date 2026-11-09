using System.Globalization;
using System.Security.Cryptography;
using HealthConnect.AuthService.Providers.Interfaces;

namespace HealthConnect.AuthService.Providers.Implementations;

/// <summary>A random 6-digit code from the operating system's secure random numbers (never System.Random).</summary>
public sealed class CodeGenerator : ICodeGenerator
{
    public const int Length = 6;

    public string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}
