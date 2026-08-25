using ClinicNow.Model.Common;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Appointments.AppointmentStateMachine;

/// <summary>The <see cref="AppointmentStatus.Pending"/> state ("Scheduled" per CLAUDE.md §8 - the two names are synonyms for the same status).</summary>
public class ScheduledAppointmentState : BaseAppointmentState
{
    public ScheduledAppointmentState(ClinicNowContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override IReadOnlyList<AppointmentAction> AllowedActions() => [AppointmentAction.Confirm, AppointmentAction.Cancel];

    public override async Task<Appointment> ConfirmAsync(Appointment appointment, int actingUserId, CancellationToken cancellationToken)
    {
        AddAuditLog(appointment, AppointmentStatus.Confirmed, actingUserId, "Termin potvrđen.");
        await Context.SaveChangesAsync(cancellationToken);
        return appointment;
    }

    public override async Task<Appointment> CancelAsync(Appointment appointment, int actingUserId, string reason, bool enforceCutoff, CancellationToken cancellationToken)
    {
        ValidateAndApplyCancel(appointment, actingUserId, reason, enforceCutoff);
        await Context.SaveChangesAsync(cancellationToken);
        return appointment;
    }
}
