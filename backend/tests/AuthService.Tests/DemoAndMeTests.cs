using System.Security.Claims;
using HealthConnect.AuthService.Features;
using HealthConnect.AuthService.Tests.Support;
using HealthConnect.Data;
using HealthConnect.Data.Entities;
using HealthConnect.SharedLibrary;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthConnect.AuthService.Tests;

/// <summary>The five synthetic demo workers: the same people, ids and numbers the app's offline demo mode has.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class DemoUsersSeederTests(AuthDatabase database) : AuthTestBase(database)
{
    private DemoUsersSeeder Seeder(bool enabled) =>
        new(Options.Create(new DemoOptions { Enabled = enabled }), Time, NullLogger<DemoUsersSeeder>.Instance);

    private async Task SeedAsync(bool enabled = true)
    {
        await using var db = Database.NewContext();
        await Seeder(enabled).SeedAsync(db, CancellationToken.None);
    }

    private async Task<List<AppUser>> UsersAsync()
    {
        await using var db = Database.NewContext();
        return await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
    }

    [Fact]
    public async Task Nothing_is_added_while_demo_mode_is_off()
    {
        await SeedAsync(enabled: false);

        Assert.Empty(await UsersAsync());
    }

    [Fact]
    public async Task The_five_demo_workers_match_what_the_app_knows()
    {
        await SeedAsync();

        var users = await UsersAsync();

        Assert.Equal(
            [
                ("auditor-02", "+233200000105", "Adwoa Boakye", "admin"),
                ("chv-042", "+233200000101", "Akosua Mensah", "chv"),
                ("dr-eshun", "+233200000103", "Kwesi Eshun", "doctor"),
                ("ghs-officer-07", "+233200000104", "Yaw Darko", "district_officer"),
                ("nurse-17", "+233200000102", "Efua Asante", "nurse"),
            ],
            users.Select(u => (u.Id, u.Phone, u.Name, u.Role)));
        Assert.All(users, u =>
        {
            Assert.True(u.Demo);
            Assert.True(u.Active);
            Assert.Equal("Shai-Osudoku", u.District);
        });
    }

    [Fact]
    public async Task Every_role_the_app_knows_has_a_demo_worker()
    {
        await SeedAsync();

        Assert.Equal(Roles.All.Order(), (await UsersAsync()).Select(u => u.Role).Order());
    }

    [Fact]
    public async Task Running_it_again_adds_nothing_and_changes_nothing()
    {
        await SeedAsync();
        await using (var db = Database.NewContext())
        {
            await db.Users.Where(u => u.Id == "chv-042").ExecuteUpdateAsync(s => s.SetProperty(u => u.Name, "Renamed On Purpose").SetProperty(u => u.Active, false));
        }

        await SeedAsync();

        var users = await UsersAsync();
        Assert.Equal(5, users.Count);
        var changed = users.Single(u => u.Id == "chv-042");
        Assert.Equal("Renamed On Purpose", changed.Name);   // a person's real edits are never overwritten
        Assert.False(changed.Active);
    }

    [Fact]
    public async Task A_number_that_already_belongs_to_someone_else_is_left_alone()
    {
        await AddWorkerAsync("real-worker", "+233200000101");

        await SeedAsync();

        var users = await UsersAsync();
        Assert.Equal(5, users.Count);                       // the other four were still added
        Assert.False(users.Single(u => u.Phone == "+233200000101").Demo);
        Assert.DoesNotContain(users, u => u.Id == "chv-042");
    }
}

/// <summary>GET /api/auth/me.</summary>
[IntegrationTest]
[Collection(AuthCollection.Name)]
public sealed class GetMeTests(AuthDatabase database) : AuthTestBase(database)
{
    private static ClaimsPrincipal Caller(string? id, string? role = "chv", string name = "x", string district = "d") =>
        new(new ClaimsIdentity(
            new[] { id is null ? null : new Claim("sub", id), role is null ? null : new Claim("role", role), new Claim("name", name), new Claim("district", district) }
                .OfType<Claim>(),
            "test"));

    private async Task<Reply> MeAsync(ClaimsPrincipal principal)
    {
        await using var db = Database.NewContext();
        return await ResultReader.RunAsync(await GetMe.Handle(principal, db, CancellationToken.None));
    }

    [Fact]
    public async Task It_returns_the_worker_from_the_database_not_from_the_token()
    {
        await AddWorkerAsync();

        var reply = await MeAsync(Caller("chv-042", "admin", "Name From The Token", "Elsewhere"));   // the token is not the authority

        Assert.Equal(200, reply.Status);
        Assert.Equal("chv", reply.Body.GetProperty("role").GetString());
        Assert.Equal("Synthetic chv-042", reply.Body.GetProperty("name").GetString());
        Assert.Equal("Shai-Osudoku", reply.Body.GetProperty("district").GetString());
    }

    [Fact]
    public async Task A_worker_switched_off_since_the_token_was_made_is_signed_out()
    {
        await AddWorkerAsync(active: false);

        var reply = await MeAsync(Caller("chv-042"));

        Assert.Equal(401, reply.Status);
        Assert.Equal("unknown_user", reply.Error);
    }

    [Fact]
    public async Task A_worker_who_no_longer_exists_is_signed_out()
    {
        var reply = await MeAsync(Caller("ghost-99"));

        Assert.Equal(401, reply.Status);
    }

    [Theory]
    [InlineData(null, "chv")]
    [InlineData("chv-042", null)]
    public async Task A_token_without_an_id_or_a_role_is_refused(string? id, string? role)
    {
        await AddWorkerAsync();

        var reply = await MeAsync(Caller(id, role));

        Assert.Equal(401, reply.Status);
        Assert.Equal("bad_token", reply.Error);
    }
}

/// <summary>How the service registers itself.</summary>
[UnitTest]
public sealed class AuthServiceExtensionTests
{
    [Fact]
    public void The_service_registers_its_three_endpoints_and_the_demo_seeder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HealthConnect.Data.Database(
            new HealthConnect.Data.DatabaseSettings(null, null, 5432, null, "x", null, null, null),
            NSubstitute.Substitute.For<HealthConnect.Data.IDatabaseSecretReader>()));

        services.AddAuthService(new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ImplementationType == typeof(RequestCode));
        Assert.Contains(services, d => d.ImplementationType == typeof(VerifyCode));
        Assert.Contains(services, d => d.ImplementationType == typeof(GetMe));
        Assert.Contains(services, d => d.ImplementationType == typeof(DemoUsersSeeder));
    }

    [Fact]
    public void The_endpoints_are_where_the_app_expects_them_and_open_to_everyone_except_me()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedLibrary().AddData(builder.Configuration).AddAuthService(builder.Configuration);
        var app = builder.Build();

        app.MapFeatures();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .ToDictionary(e => e.RoutePattern.RawText!);
        Assert.Equal(["POST"], endpoints["/api/auth/code"].Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(["POST"], endpoints["/api/auth/verify"].Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(["GET"], endpoints["/api/auth/me"].Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.NotNull(endpoints["/api/auth/code"].Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
        Assert.NotNull(endpoints["/api/auth/verify"].Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
        Assert.NotEmpty(endpoints["/api/auth/me"].Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
    }
}
