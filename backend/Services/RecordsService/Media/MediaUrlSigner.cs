using Amazon.S3;
using Amazon.S3.Model;

namespace HealthConnect.RecordsService.Media;

/// <summary>Where uploaded media goes: the private bucket the deploy names in MEDIA_BUCKET. Empty when the API runs without AWS (on a laptop).</summary>
public sealed record MediaOptions(string? Bucket);

/// <summary>A short-lived link the phone can PUT one file to, and the headers it must send with it (they are part of what is signed).</summary>
public sealed record SignedUpload(string Url, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

/// <summary>Makes the upload link. The file's bytes never pass through the API: the phone sends them straight to S3.</summary>
public interface IMediaUrlSigner
{
    /// <summary>A link that allows one PUT of exactly this content type and checksum to exactly this key.</summary>
    SignedUpload Sign(string bucket, string key, string mime, string sha256Hex, DateTimeOffset now, TimeSpan validFor);
}

/// <summary>Signs with the server's own AWS identity (the task role on AWS). Signing happens in this process; nothing is sent to AWS.</summary>
public sealed class S3MediaUrlSigner(Lazy<IAmazonS3> s3) : IMediaUrlSigner
{
    public const string ChecksumHeader = "x-amz-checksum-sha256";

    public S3MediaUrlSigner(IAmazonS3 s3)
        : this(new Lazy<IAmazonS3>(s3))
    {
    }

    public SignedUpload Sign(string bucket, string key, string mime, string sha256Hex, DateTimeOffset now, TimeSpan validFor)
    {
        var expires = now.Add(validFor);
        var checksum = Convert.ToBase64String(Convert.FromHexString(sha256Hex));
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expires.UtcDateTime,
            ContentType = mime,
            // No encryption setting here on purpose. Naming "aws:kms" without a key makes S3 use its
            // own AWS-managed key (aws/s3); leaving it out lets the bucket's default apply, which is
            // our customer-managed key (alias/healthconnect-data, see deploy/terraform/storage.tf).
        };
        request.Headers[ChecksumHeader] = checksum;

        return new SignedUpload(
            s3.Value.GetPreSignedURL(request),
            new Dictionary<string, string>
            {
                ["Content-Type"] = mime,
                [ChecksumHeader] = checksum,
            },
            expires);
    }
}
