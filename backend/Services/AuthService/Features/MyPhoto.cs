using System.Security.Claims;
using HealthConnect.Data.Entities;
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
/// The signed-in health worker's own profile photo, kept on the server so it is the same on every phone they sign in on.
/// Only ever the caller's own row: the id comes from the token, never from the request.
///   GET    /api/auth/me/photo   200 image/jpeg (or image/png) · 404 none yet
///   PUT    /api/auth/me/photo   body: the image itself (JPEG or PNG, at most 128 KB; the app shrinks it to ~30 KB first) · 204 · 422 not an image or too big
///   DELETE /api/auth/me/photo   204, also when there was none
/// </summary>
public sealed class MyPhoto : IFeature
{
    public const int MaxBytes = 128 * 1024;
    public const string Path = "/api/auth/me/photo";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(Path, Get)
            .WithName("GetMyPhoto")
            .WithTags("Auth")
            .RequireAuthorization(AuthPolicies.AnyWorker)
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPut(Path, Put)
            .WithName("PutMyPhoto")
            .WithTags("Auth")
            .RequireAuthorization(AuthPolicies.AnyWorker)
            .Accepts<byte[]>("image/jpeg", "image/png")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete(Path, Delete)
            .WithName("DeleteMyPhoto")
            .WithTags("Auth")
            .RequireAuthorization(AuthPolicies.AnyWorker)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    /// <summary>"image/jpeg" or "image/png" from the file's first bytes, or null: the content type a phone sends is not trusted.</summary>
    public static string? ImageType(ReadOnlySpan<byte> bytes) =>
        bytes is [0xFF, 0xD8, 0xFF, ..] ? "image/jpeg"
        : bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] ? "image/png"
        : null;

    public static async Task<IResult> Get(ClaimsPrincipal principal, HttpResponse response, AppDbContext db, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } caller)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var photo = await db.UserPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == caller.Id, cancellationToken);
        if (photo is null)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, "no_photo");
        }

        // The app keeps its own copy for offline use; the browser should always ask again.
        response.Headers.CacheControl = "private, no-cache";
        return Results.File(photo.Photo, ImageType(photo.Photo) ?? "image/jpeg", lastModified: photo.UpdatedAt);
    }

    public static async Task<IResult> Put(
        ClaimsPrincipal principal,
        HttpRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } caller)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        if (request.ContentLength > MaxBytes)
        {
            return ApiResults.Validation("photo", "The photo must be at most 128 KB.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return ApiResults.Validation("photo", "The photo must be at most 128 KB.");
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        if (ImageType(bytes) is null)
        {
            return ApiResults.Validation("photo", "Must be a JPEG or PNG image.");
        }

        var existing = await db.UserPhotos.FirstOrDefaultAsync(p => p.UserId == caller.Id, cancellationToken);
        if (existing is null)
        {
            db.UserPhotos.Add(new UserPhoto { UserId = caller.Id, Photo = bytes, UpdatedAt = time.GetUtcNow() });
        }
        else
        {
            existing.Photo = bytes;
            existing.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> Delete(ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } caller)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        await db.UserPhotos.Where(p => p.UserId == caller.Id).ExecuteDeleteAsync(cancellationToken);
        return Results.NoContent();
    }
}
