namespace HealthConnect.AuthService;

/// <summary>Settings under "Auth". Every one has a sensible default, so a server needs none of them.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "healthconnect-api";

    public string Audience { get; set; } = "healthconnect-app";

    /// <summary>
    /// Optional: a secret (at least 32 bytes) to sign tokens with, e.g. one kept in AWS Secrets Manager. When it is empty,
    /// which is the MVP's setting, the API makes a random key the first time it starts and keeps it in the database (auth_keys).
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>How long a token works. Seven days: a CHV works offline in a village between signals (ADR 0004).</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How long an SMS code works.</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Wrong guesses allowed on one code.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The shortest gap between two codes for the same number (the app waits 60 s before offering "Send again").</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Codes for one number in an hour; stops someone filling a stranger's phone with texts.</summary>
    public int MaxCodesPerHour { get; set; } = 6;

    /// <summary>Requests to the sign-in endpoints from one address in a minute, on top of the per-number limits.</summary>
    public int RateLimitPerMinute { get; set; } = 30;
}

/// <summary>
/// Settings under "Demo": the five synthetic workers (the same ones the app's offline demo mode has) and their fixed code.
/// Only those accounts accept the demo code; a real worker never does. This is for the MVP and the demos, where no SMS provider
/// is connected. Switch it off (Demo:Enabled = false) before real people use the system.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    public string Code { get; set; } = "123456";
}
