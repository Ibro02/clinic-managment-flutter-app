using System.Data;
using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Services.Appointments.AppointmentStateMachine;

/// <summary>
/// State pattern for the <see cref="Appointment"/> lifecycle (CLAUDE.md §8):
/// <c>Pending → Confirmed → Completed</c>, with <c>Cancelled</c> reachable from any
/// non-terminal state. Each concrete state overrides only the actions legal from
/// its own status; everything else falls through to this base, which throws a
/// clear <see cref="BusinessException"/> - so an illegal transition can never
/// silently "succeed" no matter which state forgets to guard it.
///
/// <see cref="AppointmentService"/> is the only caller: it resolves the current
/// state via <see cref="CreateState"/>, asks it to perform the transition, and
/// never mutates <see cref="Appointment.Status"/> itself. All authorization
/// (who is allowed to call this at all) happens in the service, before the state
/// is even asked - this class only enforces "is this transition legal from this
/// status" and the business rules tied to *when* it happens (e.g. the 48h cutoff).
/// </summary>
public abstract class BaseAppointmentState
{
    protected readonly ClinicNowContext Context;
    protected readonly IServiceProvider ServiceProvider;

    protected BaseAppointmentState(ClinicNowContext context, IServiceProvider serviceProvider)
    {
        Context = context;
        ServiceProvider = serviceProvider;
    }

    /// <summary>Resolves the concrete state for a given status via DI (rulebook: state classes may need scoped dependencies like the DbContext).</summary>
    public static BaseAppointmentState CreateState(AppointmentStatus status, IServiceProvider serviceProvider) => status switch
    {
        AppointmentStatus.Pending => serviceProvider.GetRequiredService<ScheduledAppointmentState>(),
        AppointmentStatus.Confirmed => serviceProvider.GetRequiredService<ConfirmedAppointmentState>(),
        AppointmentStatus.Completed => serviceProvider.GetRequiredService<CompletedAppointmentState>(),
        AppointmentStatus.Cancelled => serviceProvider.GetRequiredService<CancelledAppointmentState>(),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    /// <summary>Actions legal from this state - empty for terminal states. Surfaced to clients as <c>AppointmentDto.AllowedActions</c>.</summary>
    public virtual IReadOnlyList<AppointmentAction> AllowedActions() => [];

    public virtual Task<Appointment> ConfirmAsync(Appointment appointment, int actingUserId, CancellationToken cancellationToken) =>
        throw new BusinessException(NotAllowedMessage("potvrdi", appointment.Status));

    public virtual Task<Appointment> CompleteAsync(Appointment appointment, int actingUserId, CancellationToken cancellationToken) =>
        throw new BusinessException(NotAllowedMessage("završi", appointment.Status));

    public virtual Task<Appointment> CancelAsync(Appointment appointment, int actingUserId, string reason, bool enforceCutoff, CancellationToken cancellationToken) =>
        throw new BusinessException(NotAllowedMessage("otkaži", appointment.Status));

    public virtual Task<Appointment> RescheduleAsync(Appointment appointment, int newDoctorId, DateTime newStartUtc, int actingUserId, bool enforceCutoff, CancellationToken cancellationToken) =>
        throw new BusinessException(NotAllowedMessage("premjesti", appointment.Status));

    private static string NotAllowedMessage(string action, AppointmentStatus status) =>
        $"Akcija '{action}' nije dozvoljena za termin u statusu '{status.ToDisplayName()}'.";

    /// <summary>
    /// True once <paramref name="appointment"/>'s start time has passed. The one
    /// definition of "has this appointment happened yet" (review item C9) -
    /// Confirm requires it to be false, Complete requires it to be true, and
    /// <c>AppointmentService.MapToDto</c> reads the same predicate to filter
    /// <c>AllowedActions</c>, so the UI is never offered a button the server
    /// would then reject.
    /// </summary>
    public static bool HasStarted(Appointment appointment) => DateTime.UtcNow >= appointment.StartUtc;

    /// <summary>
    /// True when fewer than 48 hours remain before <paramref name="appointment"/>
    /// starts (or it has already started) - the patient-initiated cancellation
    /// cutoff (rulebook §7). Shared between the hard check in
    /// <see cref="ValidateAndApplyCancel"/> and the <c>AllowedActions</c>
    /// filtering in <c>AppointmentService.MapToDto</c>, for the same reason as
    /// <see cref="HasStarted"/>.
    /// </summary>
    public static bool IsWithinCancellationCutoff(Appointment appointment) =>
        appointment.StartUtc - DateTime.UtcNow < TimeSpan.FromHours(48);

    /// <summary>
    /// Shared cancellation logic (reason required, optional 48h cutoff) used by
    /// every non-terminal state's <c>CancelAsync</c> override - one place to get
    /// the business rule right instead of duplicating it per state.
    /// </summary>
    protected async Task ValidateAndApplyCancelAsync(Appointment appointment, int actingUserId, string reason, bool enforceCutoff, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("reason", "Razlog otkazivanja je obavezan.");
        }

        if (enforceCutoff && IsWithinCancellationCutoff(appointment))
        {
            throw new BusinessException("Otkazivanje je moguće najkasnije 48 sati prije termina.");
        }

        appointment.CancellationReason = reason.Trim();
        AddAuditLog(appointment, AppointmentStatus.Cancelled, actingUserId, reason.Trim());
        await ArchiveResultingReferralIfAnyAsync(appointment, cancellationToken);
    }

    /// <summary>
    /// Once an appointment reaches a terminal state that means "this
    /// visit is done or moot" (Completed here, or Cancelled via
    /// <see cref="ValidateAndApplyCancelAsync"/>), any <see cref="Referral"/>
    /// it fulfilled - see <c>AppointmentService.ScheduleAsync</c>'s
    /// <c>ReferralId</c> handling - has served its purpose and is archived
    /// automatically (soft-deleted; never removed from the database, so
    /// review item C5's "stays part of the medical history" still holds -
    /// "Arhiva" in both Flutter clients is just a separate view over the
    /// same data). A no-op when this appointment didn't result from a
    /// referral.
    /// </summary>
    protected async Task ArchiveResultingReferralIfAnyAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var referral = await Context.Referrals.SingleOrDefaultAsync(r => r.ResultingAppointmentId == appointment.Id, cancellationToken);
        if (referral is not null)
        {
            referral.IsDeleted = true;
            referral.DeletedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The full availability check shared by a new booking
    /// (<see cref="InitialAppointmentState.ScheduleAsync"/>) and a reschedule
    /// (<see cref="RescheduleCoreAsync"/>, review item C6): patient/doctor/service
    /// exist, the doctor is qualified for the service (review item C2), the slot
    /// falls within working hours and outside any block (both compared in
    /// clinic-local time, review item C1), and neither the doctor nor the patient
    /// already has another active appointment overlapping it. One definition, so
    /// a reschedule can never be validated more loosely than a fresh booking -
    /// the same reasoning behind <see cref="DoctorCompatibility"/> being a single
    /// predicate rather than two.
    /// </summary>
    /// <param name="excludeAppointmentId">
    /// The appointment being rescheduled, excluded from the overlap checks
    /// against its own current row - <c>null</c> for a brand-new booking, which
    /// has no row yet.
    /// </param>
    protected async Task<(DateTime EndUtc, int LocationId)> EnsureAvailableAsync(
        int patientId, int doctorId, int medicalServiceId, DateTime startUtc,
        int? excludeAppointmentId, CancellationToken cancellationToken)
    {
        var medicalService = await Context.MedicalServices.FindAsync([medicalServiceId], cancellationToken)
            ?? throw new ValidationException("medicalServiceId", "Odabrana usluga ne postoji.");

        // Doctor.LocationId, not a client-supplied field: a doctor practices at
        // exactly one clinic (1:1), so the appointment's location is always
        // derived from the chosen doctor, never picked independently.
        var doctor = await Context.Doctors.FindAsync([doctorId], cancellationToken)
            ?? throw new ValidationException("doctorId", "Odabrani doktor ne postoji.");

        if (!await Context.Patients.AnyAsync(p => p.Id == patientId, cancellationToken))
        {
            throw new ValidationException("patientId", "Odabrani pacijent ne postoji.");
        }

        if (!await DoctorCompatibility.CanPerformAsync(Context, doctorId, medicalService.SpecializationId, cancellationToken))
        {
            throw new ValidationException("medicalServiceId", DoctorCompatibility.NotQualifiedMessage);
        }

        if (startUtc <= DateTime.UtcNow)
        {
            throw new ValidationException("startUtc", "Termin mora biti zakazan u budućnosti.");
        }

        var endUtc = startUtc.AddMinutes(medicalService.DurationMinutes);

        var dayOfWeek = ClinicTimeZone.LocalDayOfWeekOf(startUtc);
        var startTime = ClinicTimeZone.LocalTimeOf(startUtc);
        var endTime = ClinicTimeZone.LocalTimeOf(endUtc);

        var withinWorkingHours = await Context.WorkingHoursEntries.AnyAsync(w =>
            w.DoctorId == doctorId && w.DayOfWeek == dayOfWeek &&
            w.StartTime <= startTime && w.EndTime >= endTime, cancellationToken);
        if (!withinWorkingHours)
        {
            throw new BusinessException("Odabrani termin je izvan radnog vremena doktora.");
        }

        var isBlocked = await Context.ScheduleBlocks.AnyAsync(b =>
            b.DoctorId == doctorId && b.StartUtc < endUtc && b.EndUtc > startUtc, cancellationToken);
        if (isBlocked)
        {
            throw new BusinessException("Doktor nije dostupan u odabranom terminu (blokada rasporeda).");
        }

        var doctorOverlap = await Context.Appointments.AnyAsync(a =>
            a.Id != (excludeAppointmentId ?? 0) &&
            a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled &&
            a.StartUtc < endUtc && a.EndUtc > startUtc, cancellationToken);
        if (doctorOverlap)
        {
            throw new BusinessException("Doktor već ima zakazan termin u odabranom periodu.");
        }

        var patientOverlap = await Context.Appointments.AnyAsync(a =>
            a.Id != (excludeAppointmentId ?? 0) &&
            a.PatientId == patientId && a.Status != AppointmentStatus.Cancelled &&
            a.StartUtc < endUtc && a.EndUtc > startUtc, cancellationToken);
        if (patientOverlap)
        {
            throw new BusinessException("Pacijent već ima zakazan termin u odabranom periodu.");
        }

        return (endUtc, doctor.LocationId);
    }

    /// <summary>
    /// The actual reschedule mechanics (review item C6), identical regardless of
    /// which non-terminal state initiated it - <see cref="ScheduledAppointmentState"/>
    /// and <see cref="ConfirmedAppointmentState"/> both delegate their
    /// <see cref="RescheduleAsync"/> override here rather than duplicating it.
    /// Re-runs the complete availability check used for a new booking, inside the
    /// same Serializable isolation as <see cref="InitialAppointmentState.ScheduleAsync"/>
    /// so the classic concurrent-double-booking race can't slip through a
    /// reschedule either. Resets status to Pending: whoever confirmed the old
    /// slot never confirmed this one.
    /// </summary>
    protected async Task<Appointment> RescheduleCoreAsync(
        Appointment appointment, int newDoctorId, DateTime newStartUtc, int actingUserId, bool enforceCutoff, CancellationToken cancellationToken)
    {
        if (enforceCutoff && IsWithinCancellationCutoff(appointment))
        {
            throw new BusinessException("Termin je moguće premjestiti najkasnije 48 sati prije zakazanog vremena.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var (endUtc, locationId) = await EnsureAvailableAsync(
            appointment.PatientId, newDoctorId, appointment.MedicalServiceId, newStartUtc,
            excludeAppointmentId: appointment.Id, cancellationToken);

        appointment.DoctorId = newDoctorId;
        appointment.LocationId = locationId;
        appointment.StartUtc = newStartUtc;
        appointment.EndUtc = endUtc;
        // Any reminder already sent was for the old time - it no longer applies
        // to the new one, so the reminder scanner must be free to send again.
        appointment.ReminderSentAtUtc = null;

        AddAuditLog(appointment, AppointmentStatus.Pending, actingUserId, "Termin premješten.");

        await Context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return appointment;
    }

    /// <summary>
    /// Sets the new status and appends the audit trail row via the navigation
    /// collection (not a raw FK) so a brand-new, not-yet-saved Appointment and
    /// its first log row can be persisted together in one <c>SaveChangesAsync</c>.
    /// </summary>
    protected static void AddAuditLog(Appointment appointment, AppointmentStatus newStatus, int actingUserId, string? description)
    {
        appointment.Status = newStatus;
        appointment.AuditLogs.Add(new AppointmentAuditLog
        {
            Status = newStatus,
            ActingUserId = actingUserId,
            OccurredAtUtc = DateTime.UtcNow,
            Description = description
        });
    }
}
