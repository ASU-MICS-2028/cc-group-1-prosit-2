namespace HealthConnect.Data.Entities;

/// <summary>A health worker who may sign in (the users table). The id is readable text, e.g. "chv-042", so the audit trail is readable.</summary>
public sealed class AppUser
{
    public string Id { get; set; } = string.Empty;

    /// <summary>E.164, e.g. +233200000101.</summary>
    public string Phone { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>chv, nurse, doctor, district_officer, admin or patient (see SharedLibrary Roles).</summary>
    public string Role { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    /// <summary>False = cannot sign in any more.</summary>
    public bool Active { get; set; } = true;

    /// <summary>True for the synthetic demo workers: the only accounts the fixed demo code can sign in.</summary>
    public bool Demo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The worker who added this one from the Health workers page; null for demo workers and patient accounts.</summary>
    public string? AddedBy { get; set; }
}
