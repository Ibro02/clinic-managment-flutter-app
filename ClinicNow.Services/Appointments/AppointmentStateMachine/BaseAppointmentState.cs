using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
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

    private static string NotAllowedMessage(string action, AppointmentStatus status) =>
        $"Akcija '{action}' nije dozvoljena za termin u statusu '{status.ToDisplayName()}'.";

    /// <summary>
    /// Shared cancellation logic (reason required, optional 48h cutoff) used by
    /// every non-terminal state's <c>CancelAsync</c> override - one place to get
    /// the business rule right instead of duplicating it per state.
    /// </summary>
    protected void ValidateAndApplyCancel(Appointment appointment, int actingUserId, string reason, bool enforceCutoff)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("reason", "Razlog otkazivanja je obavezan.");
        }

        if (enforceCutoff && appointment.StartUtc - DateTime.UtcNow < TimeSpan.FromHours(48))
        {
            throw new BusinessException("Otkazivanje je moguće najkasnije 48 sati prije termina.");
        }

        appointment.CancellationReason = reason.Trim();
        AddAuditLog(appointment, AppointmentStatus.Cancelled, actingUserId, reason.Trim());
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
