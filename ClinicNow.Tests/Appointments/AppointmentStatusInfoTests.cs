using ClinicNow.Model.Common;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// The read-only "Statusi termina" codebook tab (review item S2) is meant to be
/// live documentation of the state machine, not a hand-maintained copy that can
/// drift from it - these tests pin <see cref="AppointmentService.GetStatusInfo"/>
/// to the same <c>AllowedActions()</c> overrides <see cref="ScheduledAppointmentState"/>
/// and <see cref="ConfirmedAppointmentState"/> already expose to
/// <c>AppointmentDto</c>, so a future change to either state's transitions is
/// caught here too.
/// </summary>
public class AppointmentStatusInfoTests
{
    private static AppointmentService BuildService(ClinicNow.Services.Database.ClinicNowContext context)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => context);
        services.AddScoped<ScheduledAppointmentState>();
        services.AddScoped<ConfirmedAppointmentState>();
        services.AddScoped<CompletedAppointmentState>();
        services.AddScoped<CancelledAppointmentState>();
        var provider = services.BuildServiceProvider();

        return new AppointmentService(
            context,
            TestContextFactory.CreateMapper(),
            provider,
            TestContextFactory.CreateHttpContextAccessor(userId: 1, Roles.Administrator),
            new ThrowingNotificationService(),
            new ThrowingEmailPublisher(),
            new ThrowingPaymentService());
    }

    [Fact]
    public void GetStatusInfo_ReturnsExactlyOneRowPerStatus()
    {
        using var context = TestContextFactory.CreateContext();
        var info = BuildService(context).GetStatusInfo();

        Assert.Equal(Enum.GetValues<AppointmentStatus>().Length, info.Count);
        Assert.All(info, row => Assert.False(string.IsNullOrWhiteSpace(row.StatusName)));
        Assert.All(info, row => Assert.False(string.IsNullOrWhiteSpace(row.Description)));
    }

    [Fact]
    public void GetStatusInfo_ForPending_MatchesScheduledAppointmentStates_AllowedActions()
    {
        using var context = TestContextFactory.CreateContext();
        var pending = BuildService(context).GetStatusInfo().Single(row => row.Status == AppointmentStatus.Pending);

        Assert.Equal(["Confirm", "Cancel", "Reschedule"], pending.AllowedActions);
    }

    [Fact]
    public void GetStatusInfo_ForConfirmed_MatchesConfirmedAppointmentStates_AllowedActions()
    {
        using var context = TestContextFactory.CreateContext();
        var confirmed = BuildService(context).GetStatusInfo().Single(row => row.Status == AppointmentStatus.Confirmed);

        Assert.Equal(["Complete", "Cancel", "Reschedule"], confirmed.AllowedActions);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void GetStatusInfo_ForATerminalStatus_HasNoAllowedActions(AppointmentStatus status)
    {
        using var context = TestContextFactory.CreateContext();
        var row = BuildService(context).GetStatusInfo().Single(r => r.Status == status);

        Assert.Empty(row.AllowedActions);
    }
}
