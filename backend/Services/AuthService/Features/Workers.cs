using System.Security.Claims;
using System.Security.Cryptography;
using HealthConnect.Data.Entities;
using HealthConnect.Data.Persistence;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthConnect.AuthService.Features;

public sealed record WorkerDto(string Id, string Name, string Phone, string Role, string District, bool Active, bool Demo, string? AddedBy, DateTimeOffset CreatedAt);

public sealed record NewWorkerRequest(string? Name, string? Phone, string? Role, string? District);

public sealed record WorkerChangeRequest(string? Name, string? Role, bool? Active);

/// <summary>
/// Who may manage whom. The district officer runs the health workers of their own district: CHVs, nurses and doctors. The admin can
/// manage every district and every kind of worker. Nobody changes their own account here, so no one can lock themselves out or
/// promote themselves. Patient accounts are never listed or changed here: they come from the patient records (PatientAccounts).
/// </summary>
public static class WorkerRules
{
    public static readonly string[] OfficerMayManage = [Roles.Chv, Roles.Nurse, Roles.Doctor];

    public static bool MayManageRole(CurrentUser manager, string role) =>
        manager.Role == Roles.Admin ? Roles.Workers.Contains(role) : OfficerMayManage.Contains(role);

    public static bool MayManage(CurrentUser manager, AppUser worker) =>
        worker.Role != Roles.Patient
        && worker.Id != manager.Id
        && MayManageRole(manager, worker.Role)
        && (manager.Role == Roles.Admin || worker.District == manager.District);

    public static WorkerDto ToDto(AppUser u) => new(u.Id, u.Name, u.Phone, u.Role, u.District, u.Active, u.Demo, u.AddedBy, u.CreatedAt);

    /// <summary>A readable, unguessable id: the role and six random letters or digits, e.g. chv-k3f9q2.</summary>
    public static string NewId(string role)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var prefix = role switch { Roles.DistrictOfficer => "officer", Roles.Doctor => "dr", _ => role };
        return $"{prefix}-{RandomNumberGenerator.GetString(alphabet, 6)}";
    }

    public static async Task AuditAsync(AppDbContext db, CurrentUser by, string workerId, string detail, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO access_log (accessed_by, role, action, outcome, purpose_of_use, entity, entity_id, detail)
                VALUES ({by.Id}, {by.Role}, 'write', 'success', 'HOPERAT', 'worker', {workerId}, {detail})
                """,
                cancellationToken);
        }
        catch (Exception problem) when (problem is not OperationCanceledException)
        {
            logger.LogError(problem, "Could not write the change to worker {WorkerId} to the audit log", workerId);
        }
    }
}

/// <summary>GET /api/workers: the health workers the caller manages (their district, or every district for the admin).</summary>
public sealed class GetWorkers : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workers", Handle)
            .WithName("GetWorkers")
            .WithTags("Workers")
            .RequireAuthorization(AuthPolicies.ManageWorkers)
            .Produces<List<WorkerDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

    public static async Task<IResult> Handle(ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } me)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var query = db.Users.AsNoTracking().Where(u => u.Role != Roles.Patient);
        if (me.Role != Roles.Admin)
        {
            query = query.Where(u => u.District == me.District);
        }

        var workers = await query.OrderBy(u => u.District).ThenByDescending(u => u.Active).ThenBy(u => u.Name).ToListAsync(cancellationToken);
        return Results.Ok(workers.Select(WorkerRules.ToDto).ToList());
    }
}

/// <summary>
/// POST /api/workers { name, phone, role, district }: adds a health worker, who can then sign in with a code sent to that phone.
/// The district officer's new workers are always in the officer's own district. 409 when the number is already someone's.
/// </summary>
public sealed class PostWorker : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/workers", Handle)
            .WithName("PostWorker")
            .WithTags("Workers")
            .RequireAuthorization(AuthPolicies.ManageWorkers)
            .Produces<WorkerDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        NewWorkerRequest request, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, ILogger<PostWorker> logger, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } me)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 100)
        {
            errors["name"] = "Enter the worker's full name.";
        }

        if (!PhoneNumber.TryParse(request.Phone, out var phone))
        {
            errors["phone"] = "Enter a Ghana mobile number.";
        }

        var role = request.Role?.Trim() ?? "";
        if (!WorkerRules.MayManageRole(me, role))
        {
            errors["role"] = me.Role == Roles.Admin ? "Choose a role." : "Choose CHV, nurse or doctor.";
        }

        var district = me.Role == Roles.Admin ? request.District?.Trim() ?? "" : me.District;
        if (district.Length is < 2 or > 100)
        {
            errors["district"] = "Enter the district.";
        }

        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == phone.E164, cancellationToken);
        if (existing is not null)
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, existing.Role == Roles.Patient ? "number_is_a_patients" : "number_in_use");
        }

        var worker = new AppUser
        {
            Id = WorkerRules.NewId(role),
            Phone = phone.E164,
            Name = name,
            Role = role,
            District = district,
            Active = true,
            CreatedAt = time.GetUtcNow(),
            AddedBy = me.Id,
        };
        db.Users.Add(worker);
        await db.SaveChangesAsync(cancellationToken);
        await WorkerRules.AuditAsync(db, me, worker.Id, $"added {role} in {district}", logger, cancellationToken);
        return Results.Created($"/api/workers/{worker.Id}", WorkerRules.ToDto(worker));
    }
}

/// <summary>
/// PATCH /api/workers/{id} { name?, role?, active? }: renames, changes the role of, or switches off (and back on) a worker the caller
/// manages. A switched-off worker is signed out everywhere at their next request and cannot sign in again.
/// </summary>
public sealed class PatchWorker : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/workers/{id}", Handle)
            .WithName("PatchWorker")
            .WithTags("Workers")
            .RequireAuthorization(AuthPolicies.ManageWorkers)
            .Produces<WorkerDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        string id, WorkerChangeRequest request, ClaimsPrincipal principal, AppDbContext db, ILogger<PatchWorker> logger, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { } me)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        var worker = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role != Roles.Patient, cancellationToken);
        if (worker is null || (me.Role != Roles.Admin && worker.District != me.District))
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, "not_found");
        }

        if (!WorkerRules.MayManage(me, worker))
        {
            return ApiResults.Error(StatusCodes.Status403Forbidden, worker.Id == me.Id ? "not_yourself" : "forbidden");
        }

        var changes = new List<string>();
        if (request.Name is { } name)
        {
            name = name.Trim();
            if (name.Length is < 2 or > 100)
            {
                return ApiResults.Validation("name", "Enter the worker's full name.");
            }

            if (name != worker.Name)
            {
                worker.Name = name;
                changes.Add("renamed");
            }
        }

        if (request.Role is { } role && role != worker.Role)
        {
            if (!WorkerRules.MayManageRole(me, role))
            {
                return ApiResults.Validation("role", me.Role == Roles.Admin ? "Choose a role." : "Choose CHV, nurse or doctor.");
            }

            changes.Add($"role {worker.Role} -> {role}");
            worker.Role = role;
        }

        if (request.Active is { } active && active != worker.Active)
        {
            worker.Active = active;
            changes.Add(active ? "switched on" : "switched off");
        }

        if (changes.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await WorkerRules.AuditAsync(db, me, worker.Id, string.Join(", ", changes), logger, cancellationToken);
        }

        return Results.Ok(WorkerRules.ToDto(worker));
    }
}
