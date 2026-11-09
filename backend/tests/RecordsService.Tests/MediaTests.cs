using System.Web;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Media;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HealthConnect.RecordsService.Tests;

/// <summary>POST /api/media/{id}/upload-url: the 5-minute link a phone uses to send a consent recording or a photo straight to the private bucket.</summary>
[IntegrationTest]
[Collection(RecordsCollection.Name)]
public sealed class MediaTests(RecordsDatabase database) : RecordsTestBase(database)
{
    private static readonly Worker Chv = RecordsDatabase.Chv;
    private static readonly Worker Nurse = RecordsDatabase.Nurse;
    private static readonly string Sha = new('a', 64);

    // Signing happens in this process with made-up keys, so these tests need no AWS account and send nothing anywhere.
    private static readonly S3MediaUrlSigner Signer = new(new AmazonS3Client(new BasicAWSCredentials("AKIATESTTESTTESTTEST", "secret-secret-secret-secret-secret-0000"), RegionEndpoint.EUWest1));

    // The signing library reads the real clock, so the clock these tests give it must be the real one.
    private readonly FakeTimeProvider _now = new(DateTimeOffset.UtcNow);

    private static MediaUploadRequest Request(Guid patient, Guid? owner = null, string kind = "consent-audio", string mime = "audio/webm", int size = 48_000, string? sha = null) => new(
        Kind: kind,
        Mime: mime,
        Size: size,
        Sha256: sha ?? Sha,
        PatientId: patient.ToString(),
        OwnerEntity: "patient",
        OwnerId: (owner ?? patient).ToString());

    private async Task<Reply> AskAsync(Guid id, MediaUploadRequest request, Worker who, string? bucket = "test-media", IMediaUrlSigner? signer = null) =>
        await ResultReader.RunAsync(await PostMediaUploadUrl.Handle(
            id, request, As(who), Sessions, signer ?? Signer, new MediaOptions(bucket), _now, NullLogger<PostMediaUploadUrl>.Instance, CancellationToken.None));

    [Fact]
    public async Task A_link_is_issued_for_one_exact_file_and_the_request_is_recorded()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var reply = await AskAsync(id, Request(patient), Chv);

        Assert.Equal(201, reply.Status);
        var url = new Uri(reply.Body.GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.StartsWith("test-media.s3.", url.Host);
        Assert.Equal($"/media/{patient}/{id}", url.AbsolutePath);
        Assert.Equal($"media/{patient}/{id}", reply.Body.GetProperty("key").GetString());
        Assert.Equal("300", query["X-Amz-Expires"]);
        var signed = query["X-Amz-SignedHeaders"]!.Split(';');
        Assert.Contains("content-type", signed);
        Assert.Contains("x-amz-checksum-sha256", signed);
        Assert.Contains("x-amz-server-side-encryption", signed);
        Assert.Equal("aws:kms", reply.Body.GetProperty("headers").GetProperty("x-amz-server-side-encryption").GetString());
        Assert.Equal("audio/webm", reply.Body.GetProperty("headers").GetProperty("Content-Type").GetString());
        Assert.Equal(Convert.ToBase64String(Convert.FromHexString(Sha)), reply.Body.GetProperty("headers").GetProperty("x-amz-checksum-sha256").GetString());
        Assert.Equal(_now.GetUtcNow().AddMinutes(5), reply.Body.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(("chv-042", "consent-audio", 48_000), await Row(id));
    }

    private async Task<(string By, string Kind, int Size)> Row(Guid id) =>
        (await QueryAsync<(string By, string Kind, int Size)>("SELECT requested_by AS By, kind AS Kind, size_bytes AS Size FROM media WHERE id = @id", new { id })).Single();

    [Fact]
    public async Task Asking_again_for_the_same_file_gives_a_fresh_link_and_one_row()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await AskAsync(id, Request(patient), Chv);

        var again = await AskAsync(id, Request(patient), Chv);

        Assert.Equal(200, again.Status);
        Assert.NotNull(again.Body.GetProperty("url").GetString());
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM media"));
    }

    [Fact]
    public async Task The_same_file_asked_for_twice_at_the_same_moment_is_recorded_once()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();

        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AskAsync(id, Request(patient), Chv)));

        Assert.All(replies, r => Assert.Contains(r.Status, new[] { 200, 201 }));
        Assert.Equal(1, replies.Count(r => r.Status == 201));
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM media"));
    }

    [Fact]
    public async Task A_different_file_under_the_same_id_is_a_conflict_and_gets_no_link()
    {
        var patient = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await AskAsync(id, Request(patient), Chv);

        var reply = await AskAsync(id, Request(patient, sha: new string('b', 64)), Chv);

        Assert.Equal(409, reply.Status);
        Assert.Equal(Sha, await ScalarAsync<string>("SELECT sha256 FROM media WHERE id = @id", new { id }));
    }

    public static TheoryData<string, Func<MediaUploadRequest, MediaUploadRequest>> BadRequests => new()
    {
        { "kind", r => r with { Kind = "selfie" } },
        { "mime", r => r with { Mime = "video/mp4" } },
        { "mime", r => r with { Mime = "image/gif" } },
        { "mime", r => r with { Mime = "audio/" } },
        { "mime", r => r with { Mime = "audio/webm; codecs=opus" } },
        { "mime", r => r with { Mime = "image/png" } },                                   // a recording must be audio
        { "mime", r => r with { Kind = "wound-photo", Mime = "audio/webm" } },            // a photo must be an image
        { "mime", r => r with { Kind = "consent-signature", Mime = "audio/webm" } },
        { "size", r => r with { Size = 0 } },
        { "size", r => r with { Size = -1 } },
        { "size", r => r with { Size = PostMediaUploadUrl.MaxBytes + 1 } },
        { "sha256", r => r with { Sha256 = new string('A', 64) } },
        { "sha256", r => r with { Sha256 = "abc" } },
        { "sha256", r => r with { Sha256 = new string('g', 64) } },
        { "patientId", r => r with { PatientId = "not-a-guid" } },
        { "ownerEntity", r => r with { OwnerEntity = "user" } },
        { "ownerId", r => r with { OwnerId = "nope" } },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task Something_wrong_in_the_request_is_a_422_that_names_the_field_and_records_nothing(string field, Func<MediaUploadRequest, MediaUploadRequest> spoil)
    {
        var patient = await SavedPatientAsync();

        var reply = await AskAsync(Guid.NewGuid(), spoil(Request(patient)), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError(field));
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM media"));
    }

    [Theory]
    [InlineData("consent-audio", "audio/webm")]
    [InlineData("consent-audio", "audio/ogg")]
    [InlineData("consent-audio", "audio/mp4")]
    [InlineData("consent-signature", "image/png")]
    [InlineData("consent-photo", "image/jpeg")]
    [InlineData("wound-photo", "image/jpeg")]
    public async Task Each_kind_of_file_is_accepted_with_its_kind_of_content(string kind, string mime)
    {
        var patient = await SavedPatientAsync();

        var reply = await AskAsync(Guid.NewGuid(), Request(patient, kind: kind, mime: mime), Chv);

        Assert.Equal(201, reply.Status);
    }

    [Fact]
    public async Task Two_megabytes_exactly_is_the_largest_file_allowed()
    {
        var patient = await SavedPatientAsync();

        var reply = await AskAsync(Guid.NewGuid(), Request(patient, size: PostMediaUploadUrl.MaxBytes), Chv);

        Assert.Equal(201, reply.Status);
    }

    [Fact]
    public async Task A_patient_who_is_not_on_the_server_yet_is_a_422_so_the_phone_keeps_the_file_and_tries_again()
    {
        var reply = await AskAsync(Guid.NewGuid(), Request(Guid.NewGuid()), Chv);

        Assert.Equal(422, reply.Status);
        Assert.NotNull(reply.FieldError("patientId"));
    }

    [Fact]
    public async Task A_CHV_gets_no_link_for_a_patient_in_another_district_but_a_nurse_does()
    {
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");

        var chv = await AskAsync(Guid.NewGuid(), Request(elsewhere), Chv);
        var nurse = await AskAsync(Guid.NewGuid(), Request(elsewhere), Nurse);

        Assert.Equal(422, chv.Status);
        Assert.Equal(201, nurse.Status);
        Assert.Equal(1, await ScalarAsync<int>("SELECT count(*) FROM media"));
    }

    [Fact]
    public async Task A_file_id_already_used_for_a_patient_the_caller_cannot_see_is_refused_without_a_link()
    {
        var elsewhere = await SavedPatientAsync(Nurse, district: "Kintampo North");
        var mine = await SavedPatientAsync();
        var id = Guid.NewGuid();
        await AskAsync(id, Request(elsewhere), Nurse);

        var reply = await AskAsync(id, Request(mine), Chv);

        Assert.Equal(409, reply.Status);
    }

    [Fact]
    public async Task Without_a_bucket_configured_the_answer_is_503_and_nothing_is_recorded()
    {
        var patient = await SavedPatientAsync();

        var none = await AskAsync(Guid.NewGuid(), Request(patient), Chv, bucket: null);
        var blank = await AskAsync(Guid.NewGuid(), Request(patient), Chv, bucket: " ");

        Assert.Equal(503, none.Status);
        Assert.Equal(503, blank.Status);
        Assert.Equal(0, await ScalarAsync<int>("SELECT count(*) FROM media"));
    }

    [Fact]
    public async Task A_worker_switched_off_and_a_token_without_a_worker_are_both_refused()
    {
        var patient = await SavedPatientAsync();
        await ScalarAsync<int>("UPDATE users SET active = false WHERE id = 'chv-042' RETURNING 1");

        var off = await AskAsync(Guid.NewGuid(), Request(patient), Chv);
        var nobody = await ResultReader.RunAsync(await PostMediaUploadUrl.Handle(
            Guid.NewGuid(), Request(patient), new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), Sessions, Signer,
            new MediaOptions("test-media"), _now, NullLogger<PostMediaUploadUrl>.Instance, CancellationToken.None));

        Assert.Equal(401, off.Status);
        Assert.Equal(401, nobody.Status);
    }
}
