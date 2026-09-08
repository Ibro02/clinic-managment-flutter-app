using ClinicNow.Model.Common;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// The audit trail (<c>AppointmentAuditLog</c>) has always been written
/// correctly, but nothing ever surfaced it through the API - a reviewer
/// inspecting through the UI saw no audit at all, even though rulebook §7
/// requires one. <see cref="AppointmentDto.AuditLogs"/> is now populated by
/// the detail endpoint (<see cref="AppointmentService.GetByIdAsync"/>) and
/// deliberately left empty by the paged list endpoint (rulebook Part II
/// §8.2: list DTOs stay display-only).
/// </summary>
public class AppointmentAuditLogTests
{
    private static AppointmentService BuildService(ClinicNow.Services.Database.ClinicNowContext context, int userId, string role)
    {
        // Unlike the empty-provider helpers elsewhere in this test project
        // (used only where the assertion is on a rejection thrown before the
        // state machine is ever touched), these tests exercise MapToDto's
        // AllowedActions computation on a real, successfully-loaded
        // appointment - so the state classes it resolves via
        // BaseAppointmentState.CreateState must actually be registered, the
        // same four states Program.cs registers.
        var services = new ServiceCollection();
        services.AddScoped(_ => context);
        services.AddScoped<ScheduledAppointmentState>();
        services.AddScoped<ConfirmedAppointmentState>();
        services.AddScoped<CompletedAppointmentState>();
        services.AddScoped<CancelledAppointmentState>();
        var provider = services.BuildServiceProvider();
        var httpContextAccessor = TestContextFactory.CreateHttpContextAccessor(userId, role);

        return new AppointmentService(
            context,
            TestContextFactory.CreateMapper(),
            provider,
            httpContextAccessor,
            new ThrowingNotificationService(),
            new ThrowingEmailPublisher(),
            new ThrowingPaymentService());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsAuditLogHistory()
    {
        using var context = TestContextFactory.CreateContext();
        var service = BuildService(context, userId: 1, role: Roles.Administrator);

        // Seeded Appointment 1 carries one seeded AppointmentAuditLog row
        // (AppointmentAuditLogConfiguration: Id=1, Status=Completed,
        // Description="Termin završen.").
        var dto = await service.GetByIdAsync(1);

        Assert.NotNull(dto);
        var entry = Assert.Single(dto!.AuditLogs);
        Assert.Equal(AppointmentStatus.Completed, entry.Status);
        Assert.Equal("Termin završen.", entry.Description);
        Assert.False(string.IsNullOrWhiteSpace(entry.ActingUserName));
    }

    [Fact]
    public async Task GetPagedAsync_LeavesAuditLogsEmpty()
    {
        using var context = TestContextFactory.CreateContext();
        var service = BuildService(context, userId: 1, role: Roles.Administrator);

        var result = await service.GetPagedAsync(new AppointmentSearchObject { PageSize = 100 });

        // Seeded Appointments 1-5 each carry a seeded audit row, so this only
        // passes if the list endpoint deliberately skips populating it.
        Assert.NotEmpty(result.ResultList);
        Assert.All(result.ResultList, a => Assert.Empty(a.AuditLogs));
    }
}
