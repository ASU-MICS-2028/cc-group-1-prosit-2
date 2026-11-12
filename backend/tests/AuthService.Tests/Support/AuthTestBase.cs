using System.Text.Json;
using HealthConnect.AuthService.Features;
using HealthConnect.AuthService.Models;
using HealthConnect.AuthService.Providers.Implementations;
using HealthConnect.AuthService.Providers.Interfaces;
using HealthConnect.Data;
using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Sms;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;

namespace HealthConnect.AuthService.Tests.Support;

[CollectionDefinition(Name)]
public sealed class AuthCollection : ICollectionFixture<AuthDatabase>
{
    public const string Name = "auth";
}

/// <summary>A real PostgreSQL with every migration applied. Each test starts with no workers, codes or signing key.</summary>
public sealed class AuthDatabase : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.ConnectionString).Options);

    public Database NewDatabase() =>
        new(new DatabaseSettings(_postgres.ConnectionString, null, 5432, null, "x", null, null, null), Substitute.For<IDatabaseSecretReader>());

    /// <summary>What is in the auth_keys table, read directly (the table has no EF entity: only the signing-key provider uses it).</summary>
    public async Task<List<byte[]>> AuthKeysAsync()
    {
        await using var source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var command = source.CreateCommand("SELECT signing_key FROM auth_keys");
        await using var reader = await command.ExecuteReaderAsync();
        var keys = new List<byte[]>();
        while (await reader.ReadAsync())
        {
            keys.Add((byte[])reader[0]);
        }

        return keys;
    }

    public async Task ResetAsync()
    {
        await using var source = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var command = source.CreateCommand("TRUNCATE user_photos, users, login_codes, auth_keys, patients CASCADE");
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>An SMS "sender" that keeps the messages, so a test can read the code the person would have received.</summary>
public sealed class CapturingSms : ISmsSender
{
    public List<(string Phone, string Message)> Sent { get; } = [];

    public bool Fail { get; set; }

    public string LastCode => System.Text.RegularExpressions.Regex.Match(Sent[^1].Message, @"\d{6}").Value;

    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("The SMS provider is down.");
        }

        Sent.Add((to.E164, message));
        return Task.CompletedTask;
    }
}

/// <summary>The pieces every sign-in test needs: a clock we can move, a fake SMS, settings, and the handlers called like the API would.</summary>
public abstract class AuthTestBase(AuthDatabase database) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    protected AuthDatabase Database { get; } = database;

    protected FakeTimeProvider Time { get; } = new(Start);

    protected CapturingSms Sms { get; } = new();

    /// <summary>Tokens are signed with a key from settings, so these tests do not need the database for it.</summary>
    protected AuthOptions Settings { get; } = new() { SigningKey = new string('k', 48) };

    protected DemoOptions Demo { get; } = new() { Enabled = false, Code = "123456" };

    public Task InitializeAsync() => Database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected ISigningKeyProvider Keys() => new SigningKeyProvider(Database.NewDatabase(), Options.Create(Settings));

    protected ICodeHasher Hasher() => new CodeHasher(Keys());

    protected ITokenIssuer Tokens() => new JwtTokenIssuer(Keys(), Options.Create(Settings), Time);

    /// <summary>A health worker row. Demo workers are the only ones the fixed demo code can sign in.</summary>
    protected async Task<AppUser> AddWorkerAsync(
        string id = "chv-042", string phone = "+233200000101", string role = "chv", bool demo = false, bool active = true)
    {
        await using var db = Database.NewContext();
        var user = new AppUser { Id = id, Phone = phone, Name = $"Synthetic {id}", Role = role, District = "Shai-Osudoku", Active = active, Demo = demo, CreatedAt = Time.GetUtcNow() };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected async Task<Reply> RequestCodeAsync(string? phone)
    {
        await using var db = Database.NewContext();
        var result = await RequestCode.Handle(
            new RequestCodeRequest(phone), db, new CodeGenerator(), Hasher(), Sms, Time, Options.Create(Settings),
            NullLogger<RequestCode>.Instance, CancellationToken.None);
        return await ResultReader.RunAsync(result);
    }

    protected async Task<Reply> VerifyAsync(string? phone, string? code)
    {
        await using var db = Database.NewContext();
        var result = await VerifyCode.Handle(
            new VerifyCodeRequest(phone, code), db, Hasher(), Tokens(), Time, Options.Create(Settings), Options.Create(Demo),
            NullLogger<VerifyCode>.Instance, CancellationToken.None);
        return await ResultReader.RunAsync(result);
    }

    protected async Task<List<LoginCode>> StoredCodesAsync()
    {
        await using var db = Database.NewContext();
        return await db.LoginCodes.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    }
}
