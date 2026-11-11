using System.Security.Claims;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Events;
using HealthConnect.RecordsService.Models;
using HealthConnect.RecordsService.Store;
using HealthConnect.RecordsService.Validation;
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
/// PUT /api/appointments/{id}: saves a visit, referral or teleconsult. This is also the referral mock: a CHV creates a referral
/// (status "requested") and a nurse or doctor accepts or declines it, then marks it seen, each by saving the appointment again.
/// <list type="bullet">
/// <item>Same repeat and version rules as patients: 201 created, 200 updated or repeated, 409 when two phones disagree.</item>
/// <item>Only a nurse or doctor may move a referral to accepted, declined or seen (403 otherwise); a referral only moves forward
/// (requested, then accepted or declined, then seen), a step back is a 409.</item>
/// <item>Who handled it and when are the signed-in worker and the clock, never what the phone says.</item>
/// <item>The patient and the type of an existing appointment cannot change.</item>
/// <item>Once saved, a new, moved or cancelled appointment is published as an event (Lab 7) for the SMS reminder and the
/// specialist notification. Publishing comes after the commit and never fails the save.</item>
/// </list>
/// </summary>
public sealed partial class PutAppointment : IFeature
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/appointments/{id:guid}", Handle)
            .WithName("PutAppointment")
            .WithTags("Appointments")
            .RequireAuthorization(AuthPolicies.Clinical)
            .Accepts<AppointmentDto>("application/json")
            .Produces<AppointmentDto>(StatusCodes.Status200OK)
            .Produces<AppointmentDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

    public static async Task<IResult> Handle(
        Guid id,
        AppointmentDto appointment,
        ClaimsPrincipal principal,
        RoleSessions sessions,
        TimeProvider time,
        ILogger<PutAppointment> logger,
        IAppointmentEvents events,
        AppointmentEventOptions eventOptions,
        CancellationToken cancellationToken)
    {
        var errors = AppointmentValidator.Check(id, appointment, time.GetUtcNow());
        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        if (CurrentUser.From(principal) is not { } user)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "bad_token");
        }

        await using var session = await sessions.TryBeginAsync(user, cancellationToken);
        if (session is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, "unknown_user");
        }

        var handlesReferrals = user.Role is Roles.Nurse or Roles.Doctor;

        try
        {
            var existing = await AppointmentStore.FindAsync(session, id, cancellationToken);
            var (result, saved) = existing is null
                ? await CreateAsync(session, id, appointment, handlesReferrals, logger, cancellationToken)
                : await UpdateAsync(session, existing, appointment, handlesReferrals, logger, cancellationToken);

            if (saved is not null && AppointmentEvent.TypeOf(existing?.Dto, saved.Value.Dto) is { } type)
            {
                await events.PublishAsync(
                    AppointmentEvent.For(type, saved.Value.Dto, saved.Value.Phone, saved.Value.Language, eventOptions.Environment, time.GetUtcNow()),
                    cancellationToken);
            }

            return result;
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden");
        }
    }

    /// <summary>What was saved, with the patient's phone and language for the event (read before the commit, under the same access rules).</summary>
    private readonly record struct Saved(AppointmentDto Dto, string? Phone, string? Language);

    private static async Task<Saved> SavedAsync(RoleSession session, StoredAppointment stored, CancellationToken cancellationToken)
    {
        var contact = await session.Connection.QuerySingleOrDefaultAsync<(string? Phone, string? Language)>(new CommandDefinition(
            "SELECT phone, preferred_language FROM patients WHERE id = @id", new { id = stored.PatientInternalId },
            session.Transaction, cancellationToken: cancellationToken));
        return new Saved(stored.Dto, contact.Phone, contact.Language);
    }

    private static async Task<(IResult, Saved?)> CreateAsync(
        RoleSession session, Guid id, AppointmentDto appointment, bool handlesReferrals, ILogger logger, CancellationToken cancellationToken)
    {
        var patientInternalId = await session.Connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM patients WHERE fhir_id = @patient", new { patient = Guid.Parse(appointment.PatientId) },
            session.Transaction, cancellationToken: cancellationToken));
        if (patientInternalId is null)
        {
            return (ApiResults.Validation("patientId", "This patient is not on the server yet."), null);
        }

        // A new referral or teleconsult starts as "requested".
        var referralStatus = Vocabulary.HasReferralStatus(appointment.Type) ? appointment.ReferralStatus ?? "requested" : null;
        if (referralStatus is not (null or "requested") && !handlesReferrals)
        {
            return (ApiResults.Error(StatusCodes.Status403Forbidden, "only_clinicians_handle_referrals"), null);
        }

        await AppointmentStore.InsertAsync(session, id, patientInternalId.Value, appointment, referralStatus, cancellationToken);
        if (referralStatus is not (null or "requested"))
        {
            await AppointmentStore.UpdateAsync(session, id, 1, appointment, referralStatus, true, cancellationToken);   // records who handled it
        }

        var saved = await SavedAsync(session, (await AppointmentStore.FindAsync(session, id, cancellationToken))!, cancellationToken);
        await session.CommitAsync(cancellationToken);
        LogSaved(logger, session.User.Id, id, "created", referralStatus);
        return (Results.Json(saved.Dto, statusCode: StatusCodes.Status201Created), saved);
    }

    private static async Task<(IResult, Saved?)> UpdateAsync(
        RoleSession session, StoredAppointment existing, AppointmentDto appointment, bool handlesReferrals, ILogger logger, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(existing.Dto.Id);
        if (appointment.PatientId != existing.Dto.PatientId)
        {
            return (ApiResults.Validation("patientId", "An appointment cannot move to another patient."), null);
        }

        if (appointment.Type != existing.Dto.Type)
        {
            return (ApiResults.Validation("type", "The type of an appointment cannot change; book a new one."), null);
        }

        // Not saying a referral status leaves it as it is (a CHV marking a referral visit done must not reset it).
        var referralStatus = Vocabulary.HasReferralStatus(existing.Dto.Type) ? appointment.ReferralStatus ?? existing.Dto.ReferralStatus : null;
        var incoming = appointment with { ReferralStatus = referralStatus };

        if (existing.Dto.Version != appointment.Version)
        {
            return (AppointmentStore.SameContent(existing.Dto, incoming) ? Results.Ok(existing.Dto) : ApiResults.Conflict(existing.Dto), null);
        }

        var moved = referralStatus != existing.Dto.ReferralStatus;
        if (moved)
        {
            if (!handlesReferrals && referralStatus != "requested")
            {
                return (ApiResults.Error(StatusCodes.Status403Forbidden, "only_clinicians_handle_referrals"), null);
            }

            if (referralStatus is null || !AppointmentValidator.MayMove(existing.Dto.ReferralStatus, referralStatus))
            {
                return (ApiResults.Conflict(existing.Dto), null);   // a step back, or a step skipped: the phone is out of date
            }
        }

        if (!await AppointmentStore.UpdateAsync(session, id, existing.Dto.Version, incoming, referralStatus, moved, cancellationToken))
        {
            return (ApiResults.Conflict(existing.Dto), null);   // someone else saved it a moment ago
        }

        var saved = await SavedAsync(session, (await AppointmentStore.FindAsync(session, id, cancellationToken))!, cancellationToken);
        await session.CommitAsync(cancellationToken);
        LogSaved(logger, session.User.Id, id, "updated", referralStatus);
        return (Results.Ok(saved.Dto), saved);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Appointment {AppointmentId} {What} by {UserId} (referral status {ReferralStatus})")]
    private static partial void LogSaved(ILogger logger, string userId, Guid appointmentId, string what, string? referralStatus);
}
