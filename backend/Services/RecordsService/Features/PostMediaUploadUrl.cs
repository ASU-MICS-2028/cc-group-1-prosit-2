using System.Security.Claims;
using System.Text.RegularExpressions;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Media;
using HealthConnect.RecordsService.Models;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HealthConnect.RecordsService.Features;

/// <summary>
/// POST /api/media/{id}/upload-url: a link the phone uses to send one file (a consent recording, a signature, a wound photo) straight to the
/// private bucket. It is only issued for a patient the caller may write to, for at most 2 MB of audio, PNG or JPEG, and the link works for
/// 5 minutes, for exactly this content type and this checksum. The API records that it issued the link; asking again for the same file
/// (the phone lost the first reply, or the link expired) gives a fresh link, and asking for a different file under the same id is a conflict.
/// </summary>
public sealed partial class PostMediaUploadUrl : IFeature
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private static readonly string[] Kinds = ["consent-audio", "consent-signature", "consent-photo", "wound-photo"];
    private static readonly string[] Owners = ["patient", "vitals", "appointment"];

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    [GeneratedRegex("^audio/[a-z0-9][a-z0-9.+-]{0,60}$")]
    private static partial Regex AudioMime();

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/media/{id:guid}/upload-url", Handle)
            .WithName("PostMediaUploadUrl")
            .WithTags("Media")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Accepts<MediaUploadRequest>("application/json")
            .Produces<MediaUploadResponse>(StatusCodes.Status200OK)
            .Produces<MediaUploadResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status503ServiceUnavailable);

    public static Dictionary<string, string> Check(Guid id, MediaUploadRequest r)
    {
        var errors = new Dictionary<string, string>();
        if (!Kinds.Contains(r.Kind))
        {
            errors["kind"] = "Must be consent-audio, consent-signature, consent-photo or wound-photo.";
        }

        var isImage = r.Mime is "image/png" or "image/jpeg";
        var isAudio = r.Mime is not null && AudioMime().IsMatch(r.Mime);
        if (!isImage && !isAudio)
        {
            errors["mime"] = "Only audio, image/png and image/jpeg are accepted.";
        }
        else if (r.Kind is "consent-audio" && !isAudio)
        {
            errors["mime"] = "A consent recording must be audio.";
        }
        else if (r.Kind is "consent-signature" or "consent-photo" or "wound-photo" && !isImage)
        {
            errors["mime"] = "A signature or photo must be a PNG or JPEG image.";
        }

        if (r.Size is < 1 or > MaxBytes)
        {
            errors["size"] = "Must be between 1 byte and 2 MB.";
        }

        if (r.Sha256 is null || !Sha256().IsMatch(r.Sha256))
        {
            errors["sha256"] = "Must be 64 lowercase hexadecimal characters.";
        }

        if (!Guid.TryParse(r.PatientId, out _))
        {
            errors["patientId"] = "Must be a UUID.";
        }

        if (!Owners.Contains(r.OwnerEntity))
        {
            errors["ownerEntity"] = "Must be patient, vitals or appointment.";
        }

        if (!Guid.TryParse(r.OwnerId, out _))
        {
            errors["ownerId"] = "Must be a UUID.";
        }

        return errors;
    }

    public static async Task<IResult> Handle(
        Guid id,
        MediaUploadRequest request,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        IMediaUrlSigner signer,
        MediaOptions options,
        TimeProvider time,
        ILogger<PostMediaUploadUrl> logger,
        CancellationToken cancellationToken)
    {
        var errors = Check(id, request);
        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            return ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "media_unavailable");
        }

        var patientFhirId = Guid.Parse(request.PatientId);
        var key = $"media/{patientFhirId}/{id}";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var session = await sessions.TryBeginAsync(user, cancellationToken);
            if (session is null)
            {
                return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
            }

            try
            {
                var existing = await session.Connection.QuerySingleOrDefaultAsync<(string Key, string Kind, string Mime, int Size, string Sha256, Guid OwnerId, string OwnerEntity)?>(new CommandDefinition(
                    "SELECT s3_key AS key, kind, mime, size_bytes AS size, sha256, owner_id, owner_entity FROM media WHERE id = @id",
                    new { id }, session.Transaction, cancellationToken: cancellationToken));
                if (existing is { } known)
                {
                    var same = known.Key == key && known.Kind == request.Kind && known.Mime == request.Mime && known.Size == request.Size
                        && known.Sha256 == request.Sha256 && known.OwnerId == Guid.Parse(request.OwnerId) && known.OwnerEntity == request.OwnerEntity;
                    return same ? Issue(StatusCodes.Status200OK) : ApiResults.Error(StatusCodes.Status409Conflict, "conflict");
                }

                var patientInternalId = await session.Connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT id FROM patients WHERE fhir_id = @patientFhirId", new { patientFhirId }, session.Transaction, cancellationToken: cancellationToken));
                if (patientInternalId is null)
                {
                    return ApiResults.Validation("patientId", "This patient is not on the server yet.");
                }

                await session.Connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO media (id, patient_id, owner_entity, owner_id, kind, mime, size_bytes, sha256, s3_key, requested_by)
                    VALUES (@id, @patientInternalId, @ownerEntity, @ownerId, @kind, @mime, @size, @sha256, @key, @by)
                    """,
                    new
                    {
                        id,
                        patientInternalId,
                        ownerEntity = request.OwnerEntity,
                        ownerId = Guid.Parse(request.OwnerId),
                        kind = request.Kind,
                        mime = request.Mime,
                        size = request.Size,
                        sha256 = request.Sha256,
                        key,
                        by = user.Id,
                    },
                    session.Transaction,
                    cancellationToken: cancellationToken));
                await session.CommitAsync(cancellationToken);
                LogIssued(logger, user.Id, id);
                return Issue(StatusCodes.Status201Created);
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // The same file was asked for twice at the same moment and the other request won: look again and answer as for a repeat.
            }
            catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden");
            }
        }

        return ApiResults.Error(StatusCodes.Status409Conflict, "conflict");

        IResult Issue(int status)
        {
            var signed = signer.Sign(options.Bucket!, key, request.Mime, request.Sha256, time.GetUtcNow(), LinkLifetime);
            return Results.Json(new MediaUploadResponse(signed.Url, signed.Headers, signed.ExpiresAt, key), statusCode: status);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upload link for media {MediaId} issued to {UserId}")]
    private static partial void LogIssued(ILogger logger, string userId, Guid mediaId);
}
