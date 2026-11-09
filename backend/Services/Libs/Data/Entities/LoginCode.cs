namespace HealthConnect.Data.Entities;

/// <summary>A sign-in code sent by SMS. Only its hash is kept, so the database never holds a usable code.</summary>
public sealed class LoginCode
{
    public long Id { get; set; }

    public string Phone { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Wrong guesses so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Set the moment the code signs someone in; a code works once.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
