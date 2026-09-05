using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Appointments.AppointmentStateMachine;

public class ConfirmedAppointmentState : BaseAppointmentState
{
    public ConfirmedAppointmentState(ClinicNowContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override IReadOnlyList<AppointmentAction> AllowedActions() =>
        [AppointmentAction.Complete, AppointmentAction.Cancel, AppointmentAction.Reschedule];

    public override async Task<Appointment> CompleteAsync(Appointment appointment, int actingUserId, CancellationToken cancellationToken)
    {
        // A future termin hasn't happened yet, so it can't be "done" (review item
        // C9) - checked here, server-side, so a direct API call can't complete
        // what the UI already hides via AllowedActions.
        if (!HasStarted(appointment))
        {
            throw new BusinessException("Termin se ne može označiti kao završen prije nego što počne.");
        }

        AddAuditLog(appointment, AppointmentStatus.Completed, actingUserId, "Termin završen.");
        await ArchiveResultingReferralIfAnyAsync(appointment, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        return appointment;
    }

    public override async Task<Appointment> CancelAsync(Appointment appointment, int actingUserId, string reason, bool enforceCutoff, CancellationToken cancellationToken)
    {
        await ValidateAndApplyCancelAsync(appointment, actingUserId, reason, enforceCutoff, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        return appointment;
    }

    public override Task<Appointment> RescheduleAsync(Appointment appointment, int newDoctorId, DateTime newStartUtc, int actingUserId, bool enforceCutoff, CancellationToken cancellationToken) =>
        RescheduleCoreAsync(appointment, newDoctorId, newStartUtc, actingUserId, enforceCutoff, cancellationToken);
}
