using System.Security.Claims;
using HealthConnect.AuthService.Models;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HealthConnect.AuthService.Features;

/// <summary>
/// GET /api/auth/me: who the token says you are, checked against the users table. 200 { id, name, role, district } · 401 no or bad token,
/// or the worker has been switched off since the token was made (the app then signs the person out).
/// </summary>
public sealed class GetMe : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/auth/me", Handle)
            .WithName("GetMe")
            .WithTags("Auth")
            .RequireAuthorization(AuthPolicies.SignedIn)
            .Produces<UserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    public static async Task<IResult> Handle(ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        var caller = CurrentUser.From(principal);
        if (caller is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == caller.Id && u.Active, cancellationToken);
        return user is null
            ? ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user")
            : Results.Ok(new UserDto(user.Id, user.Name, user.Role, user.District));
    }
}
