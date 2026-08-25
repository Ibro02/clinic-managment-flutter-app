using ClinicNow.Services.Database;

namespace ClinicNow.Services.Appointments.AppointmentStateMachine;

/// <summary>Terminal state - every action falls through to the base class's "not allowed" default. No overrides needed.</summary>
public class CompletedAppointmentState : BaseAppointmentState
{
    public CompletedAppointmentState(ClinicNowContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }
}
