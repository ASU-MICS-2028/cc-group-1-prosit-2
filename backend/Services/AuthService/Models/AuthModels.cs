namespace HealthConnect.AuthService.Models;

/// <summary>POST /api/auth/code: the number to text a code to.</summary>
public sealed record RequestCodeRequest(string? Phone);

/// <summary>POST /api/auth/verify: the number and the 6-digit code.</summary>
public sealed record VerifyCodeRequest(string? Phone, string? Code);

/// <summary>The signed-in person, as the app expects it: { id, name, role, district }.</summary>
public sealed record UserDto(string Id, string Name, string Role, string District);

/// <summary>The answer to a successful sign-in: the token, when it stops working, and who it is for.</summary>
public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);
