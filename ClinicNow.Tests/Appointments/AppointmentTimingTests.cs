using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// The time predicates behind review item C9: an appointment can't be confirmed
/// once its start time has passed, and can't be completed before it starts.
/// <see cref="BaseAppointmentState.HasStarted"/> and
/// <see cref="BaseAppointmentState.IsWithinCancellationCutoff"/> are the single
/// definition both the hard server-side check (in the state classes) and the
/// AllowedActions filtering (in AppointmentService.MapToDto) read from - the
/// same "one predicate, two call sites" shape as DoctorCompatibility (C2), so
/// the UI's disabled-button state can never disagree with what the server would
/// actually accept.
/// </summary>
public class AppointmentTimingTests
{
    private static Appointment AppointmentStartingIn(TimeSpan offset) =>
        new() { StartUtc = DateTime.UtcNow + offset, EndUtc = DateTime.UtcNow + offset + TimeSpan.FromMinutes(30) };

    [Fact]
    public void HasStarted_IsFalse_ForAFutureAppointment()
    {
        Assert.False(BaseAppointmentState.HasStarted(AppointmentStartingIn(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void HasStarted_IsTrue_ForAPastAppointment()
    {
        Assert.True(BaseAppointmentState.HasStarted(AppointmentStartingIn(-TimeSpan.FromHours(1))));
    }

    [Fact]
    public void HasStarted_IsTrue_AtTheExactStartInstant()
    {
        var appointment = new Appointment { StartUtc = DateTime.UtcNow };
        Assert.True(BaseAppointmentState.HasStarted(appointment));
    }

    [Fact]
    public void IsWithinCancellationCutoff_IsTrue_WhenLessThan48HoursAway()
    {
        Assert.True(BaseAppointmentState.IsWithinCancellationCutoff(AppointmentStartingIn(TimeSpan.FromHours(10))));
    }

    [Fact]
    public void IsWithinCancellationCutoff_IsFalse_WhenAtLeast48HoursAway()
    {
        Assert.False(BaseAppointmentState.IsWithinCancellationCutoff(AppointmentStartingIn(TimeSpan.FromHours(72))));
    }

    /// <summary>An appointment that has already passed is trivially "within" the cutoff - there's no legal cancellation window left.</summary>
    [Fact]
    public void IsWithinCancellationCutoff_IsTrue_ForAnAppointmentAlreadyInThePast()
    {
        Assert.True(BaseAppointmentState.IsWithinCancellationCutoff(AppointmentStartingIn(-TimeSpan.FromHours(1))));
    }
}
