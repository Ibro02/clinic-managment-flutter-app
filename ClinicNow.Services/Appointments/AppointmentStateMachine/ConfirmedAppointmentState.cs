using ClinicNow.Model.Common;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Appointments.AppointmentStateMachine;

public class ConfirmedAppointmentState : BaseAppointmentState
{
    public ConfirmedAppointmentState(ClinicNowContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override IReadOnlyList<AppointmentAction> AllowedActions() => [AppointmentAction.Complete, AppointmentAction.Cancel];

    public override async Task<Appointment> CompleteAsync(Appointment appointment, int actingUserId, CancellationToken cancellationToken)
    {
        AddAuditLog(appointment, AppointmentStatus.Completed, actingUserId, "Termin završen.");
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
