using System.Security.Claims;
using Dapper;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Patients;
using HealthConnect.RecordsService.Store;
using HealthConnect.SharedLibrary.Features;
using HealthConnect.SharedLibrary.Http;
using HealthConnect.SharedLibrary.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HealthConnect.RecordsService.Features;

/// <summary>One person whose record carries the account's phone number (a household can share one phone).</summary>
public sealed record MyPersonDto(string Id, string Name, string? Community, string? Sex, string? DateOfBirth);

public sealed record MyHealthDto(List<MyPersonDto> People, List<AppointmentDto> Appointments, List<VitalsDto> Vitals);

/// <summary>
/// GET /api/my/health: a signed-in patient's own records: who is registered with their phone, their visits (newest first) and their
/// latest readings. Read-only; the CHV still does the recording. Never anyone else's record.
/// </summary>
public sealed class GetMyHealth : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/my/health", Handle)
            .WithName("GetMyHealth")
            .WithTags("Patients")
            .RequireAuthorization(AuthPolicies.Patient)
            .Produces<MyHealthDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

    public static async Task<IResult> Handle(ClaimsPrincipal principal, PatientRecords patients, CancellationToken cancellationToken)
    {
        if (CurrentUser.From(principal) is not { Role: Roles.Patient } user
            || await patients.PhoneAsync(user.Id, cancellationToken) is not { } phone)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
        }

        await using var connection = await patients.OpenAsync(cancellationToken);
        var people = (await connection.QueryAsync<PersonRow>(new CommandDefinition(
            "SELECT fhir_id, full_name, community, gender AS sex, date_of_birth AS birth_date FROM patients WHERE phone = @phone ORDER BY id",
            new { phone }, cancellationToken: cancellationToken)))
            .Select(p => new MyPersonDto(p.FhirId.ToString(), p.FullName, p.Community, p.Sex, p.BirthDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        var visits = await AppointmentStore.ForPhoneAsync(connection, phone, cancellationToken);
        var vitals = await VitalsStore.ForPhoneAsync(connection, phone, 50, cancellationToken);
        return Results.Ok(new MyHealthDto(people, visits, vitals));
    }

    private sealed class PersonRow
    {
        public Guid FhirId { get; set; }

        public string FullName { get; set; } = "";

        public string? Community { get; set; }

        public string? Sex { get; set; }

        public DateTime? BirthDate { get; set; }
    }
}
