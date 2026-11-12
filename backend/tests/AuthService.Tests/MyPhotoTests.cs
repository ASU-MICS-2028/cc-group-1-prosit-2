using System.Security.Claims;
using HealthConnect.AuthService.Features;
using HealthConnect.AuthService.Tests.Support;
using HealthConnect.SharedLibrary;
using HealthConnect.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HealthConnect.AuthService.Tests;

/// <summary>GET/PUT/DELETE /api/auth/me/photo: a worker's own profile photo, the same on every phone.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class MyPhotoTests(AuthDatabase database) : AuthTestBase(database)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static ClaimsPrincipal Caller(string id) =>
        new(new ClaimsIdentity([new Claim("sub", id), new Claim("role", "chv"), new Claim("name", "x"), new Claim("district", "d")], "test"));

    private static HttpRequest Body(byte[] bytes)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        return context.Request;
    }

    private async Task<Reply> PutAsync(string id, byte[] bytes)
    {
        await using var db = Database.NewContext();
        return await ResultReader.RunAsync(await MyPhoto.Put(Caller(id), Body(bytes), db, Time, CancellationToken.None));
    }

    private async Task<IResult> GetAsync(string id)
    {
        await using var db = Database.NewContext();
        return await MyPhoto.Get(Caller(id), new DefaultHttpContext().Response, db, CancellationToken.None);
    }

    [Fact]
    public async Task A_photo_saved_on_one_phone_is_what_every_phone_gets_back()
    {
        await AddWorkerAsync("chv-042");

        Assert.Equal(204, (await PutAsync("chv-042", Jpeg)).Status);

        var file = Assert.IsType<FileContentHttpResult>(await GetAsync("chv-042"));
        Assert.Equal(Jpeg, file.FileContents.ToArray());
        Assert.Equal("image/jpeg", file.ContentType);
    }

    [Fact]
    public async Task A_new_photo_replaces_the_old_one_and_delete_removes_it()
    {
        await AddWorkerAsync("chv-042");
        await PutAsync("chv-042", Jpeg);

        await PutAsync("chv-042", Png);
        Assert.Equal("image/png", Assert.IsType<FileContentHttpResult>(await GetAsync("chv-042")).ContentType);

        await using (var db = Database.NewContext())
        {
            Assert.Equal(204, (await ResultReader.RunAsync(await MyPhoto.Delete(Caller("chv-042"), db, CancellationToken.None))).Status);
        }

        Assert.Equal(404, (await ResultReader.RunAsync(await GetAsync("chv-042"))).Status);
    }

    [Fact]
    public async Task Each_worker_only_ever_sees_their_own_photo()
    {
        await AddWorkerAsync("chv-042");
        await AddWorkerAsync("dr-eshun", "+233200000103", "doctor");
        await PutAsync("chv-042", Jpeg);

        Assert.Equal(404, (await ResultReader.RunAsync(await GetAsync("dr-eshun"))).Status);
    }

    [Fact]
    public async Task Anything_that_is_not_a_small_jpeg_or_png_is_refused()
    {
        await AddWorkerAsync("chv-042");

        var text = await PutAsync("chv-042", "not an image"u8.ToArray());
        Assert.Equal(422, text.Status);
        Assert.NotNull(text.FieldError("photo"));

        var huge = new byte[MyPhoto.MaxBytes + 1];
        Jpeg.CopyTo(huge, 0);
        Assert.Equal(422, (await PutAsync("chv-042", huge)).Status);

        Assert.Equal(404, (await ResultReader.RunAsync(await GetAsync("chv-042"))).Status);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)]
    [InlineData(new byte[] { }, null)]
    public void The_type_comes_from_the_bytes_not_from_what_the_phone_says(byte[] bytes, string? type) =>
        Assert.Equal(type, MyPhoto.ImageType(bytes));
}
