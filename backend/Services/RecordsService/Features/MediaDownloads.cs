using System.Security.Claims;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Media;
using HealthConnect.RecordsService.Models;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HealthConnect.RecordsService.Features;

/// <summary>
/// GET /api/media?updatedSince=: the photos other workers took (a wound, a lab slip, an X-ray film), so a nurse or doctor sees what the
/// CHV saw. Only the description; the bytes come from <see cref="GetMediaDownloadUrl"/>. Consent evidence is not listed: it is proof
/// kept for the auditor, not something care needs on every phone. The database's district rules decide what each caller gets.
/// </summary>
public sealed class GetMediaList : IFeature
{
    /// <summary>The kinds shared between phones.</summary>
    public static readonly string[] SharedKinds = ["wound-photo", "result-photo"];

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/media", Handle)
            .WithName("GetMediaList")
            .WithTags("Media")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<List<MediaInfoDto>>(StatusCodes.Status200OK);

    public static Task<IResult> Handle(
        ClaimsPrincipal principal, RoleSessions sessions, CancellationToken cancellationToken, [FromQuery] DateTimeOffset? updatedSince = null) =>
        Pull.RunAsync(
            principal,
            sessions,
            async (session, ct) => (await session.Connection.QueryAsync<Row>(new CommandDefinition(
                """
                SELECT m.id, p.fhir_id AS patient_id, m.owner_entity, m.owner_id, m.kind, m.mime, m.size_bytes AS size, m.sha256,
                       m.requested_at AS created_at
                FROM media m JOIN patients p ON p.id = m.patient_id
                WHERE m.kind = ANY(@kinds) AND (@since::timestamptz IS NULL OR m.requested_at > @since)
                ORDER BY m.requested_at, m.id LIMIT @limit
                """,
                new { kinds = SharedKinds, since = updatedSince?.ToUniversalTime(), limit = Pull.Limit },
                session.Transaction,
                cancellationToken: ct))).Select(r => r.ToDto()).ToList(),
            cancellationToken);

    private sealed class Row
    {
        public Guid Id { get; set; }

        public Guid PatientId { get; set; }

        public string OwnerEntity { get; set; } = string.Empty;

        public Guid OwnerId { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Mime { get; set; } = string.Empty;

        public int Size { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public MediaInfoDto ToDto() => new(
            Id.ToString(), PatientId.ToString(), OwnerEntity, OwnerId.ToString(), Kind, Mime, Size, Sha256,
            new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)));
    }
}

/// <summary>
/// GET /api/media/{id}/download-url: a 5-minute link to read one shared photo. Issued only when the caller may see the patient it
/// belongs to (the media row is invisible otherwise, so the answer is 404 rather than a hint that it exists). Every link is audited by
/// the phone as a view of that record.
/// </summary>
public sealed class GetMediaDownloadUrl : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/media/{id:guid}/download-url", Handle)
            .WithName("GetMediaDownloadUrl")
            .WithTags("Media")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Produces<MediaDownloadResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

    public static async Task<IResult> Handle(
        Guid id,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        IMediaUrlSigner signer,
        MediaOptions options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            return ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "media_unavailable");
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        if (session is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
        }

        var key = await session.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT s3_key FROM media WHERE id = @id AND kind = ANY(@kinds)",
            new { id, kinds = GetMediaList.SharedKinds },
            session.Transaction,
            cancellationToken: cancellationToken));
        if (key is null)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, "not_found");
        }

        var now = time.GetUtcNow();
        return Results.Ok(new MediaDownloadResponse(
            signer.SignDownload(options.Bucket!, key, now, PostMediaUploadUrl.LinkLifetime), now.Add(PostMediaUploadUrl.LinkLifetime)));
    }
}
