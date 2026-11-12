namespace HealthConnect.Data.Entities;

/// <summary>A health worker's profile photo (the user_photos table): one small JPEG, shrunk on the phone before upload.</summary>
public sealed class UserPhoto
{
    public string UserId { get; set; } = string.Empty;

    public byte[] Photo { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}
