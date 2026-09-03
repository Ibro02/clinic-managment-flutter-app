using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// Review item C6: reschedule is legal from the two non-terminal states
/// (Pending/Scheduled and Confirmed) and illegal from the two terminal ones,
/// exactly like Confirm/Complete/Cancel already are. This covers the part of
/// C6 that doesn't need a real Serializable transaction (AllowedActions and
/// the base "not allowed" default) - the availability re-check itself
/// (working hours, blocks, overlap, doctor↔service compatibility) is verified
/// against the live API instead, the same way C1/C2's booking-path checks are
/// (EF Core InMemory can't honour Serializable isolation).
/// </summary>
public class RescheduleStateTests
{
    private static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void AllowedActions_OnScheduled_IncludesReschedule()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new ScheduledAppointmentState(context, EmptyProvider);

        Assert.Contains(AppointmentAction.Reschedule, state.AllowedActions());
    }

    [Fact]
    public void AllowedActions_OnConfirmed_IncludesReschedule()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new ConfirmedAppointmentState(context, EmptyProvider);

        Assert.Contains(AppointmentAction.Reschedule, state.AllowedActions());
    }

    [Fact]
    public void AllowedActions_OnCompleted_DoesNotIncludeReschedule()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new CompletedAppointmentState(context, EmptyProvider);

        Assert.Empty(state.AllowedActions());
    }

    [Fact]
    public void AllowedActions_OnCancelled_DoesNotIncludeReschedule()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new CancelledAppointmentState(context, EmptyProvider);

        Assert.Empty(state.AllowedActions());
    }

    [Fact]
    public async Task RescheduleAsync_OnCompleted_ThrowsBusinessException()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new CompletedAppointmentState(context, EmptyProvider);
        var appointment = new Appointment { Status = AppointmentStatus.Completed };

        await Assert.ThrowsAsync<BusinessException>(() =>
            state.RescheduleAsync(appointment, newDoctorId: 1, newStartUtc: DateTime.UtcNow.AddDays(1), actingUserId: 1, enforceCutoff: false, CancellationToken.None));
    }

    [Fact]
    public async Task RescheduleAsync_OnCancelled_ThrowsBusinessException()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new CancelledAppointmentState(context, EmptyProvider);
        var appointment = new Appointment { Status = AppointmentStatus.Cancelled };

        await Assert.ThrowsAsync<BusinessException>(() =>
            state.RescheduleAsync(appointment, newDoctorId: 1, newStartUtc: DateTime.UtcNow.AddDays(1), actingUserId: 1, enforceCutoff: false, CancellationToken.None));
    }

    /// <summary>
    /// The 48h cutoff must be checked before anything else (including a DB
    /// round-trip for availability) - a patient trying to move an appointment
    /// that starts in 2 hours should get the cutoff message, not an
    /// unrelated availability error.
    /// </summary>
    [Fact]
    public async Task RescheduleAsync_WithinCutoffAndEnforced_ThrowsBeforeCheckingAvailability()
    {
        using var context = TestContextFactory.CreateContext();
        var state = new ScheduledAppointmentState(context, EmptyProvider);
        var appointment = new Appointment { Status = AppointmentStatus.Pending, StartUtc = DateTime.UtcNow.AddHours(2) };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            state.RescheduleAsync(appointment, newDoctorId: 999, newStartUtc: DateTime.UtcNow.AddDays(10), actingUserId: 1, enforceCutoff: true, CancellationToken.None));

        Assert.Contains("48", ex.Message);
    }
}
