using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// A doctor may move their own appointment to another time, but not onto a
/// colleague - reassigning it would commit another doctor to work they never
/// agreed to take. <c>EnsureDoctorOwnershipIfApplicableAsync</c> alone does not
/// cover this: it proves the appointment is the caller's <em>now</em> and says
/// nothing about the target, so a doctor could previously pass ownership and
/// still hand the appointment away in the same request.
///
/// The gate sits in <c>AppointmentService.RescheduleAsync</c>, before
/// <c>ScheduledAppointmentState.RescheduleAsync</c> opens its Serializable
/// transaction - which is what makes the rejection testable against EF InMemory
/// at all. A <em>successful</em> reschedule is not (InMemory has no Serializable
/// isolation), the same split C1/C5/C6 already established, so the second test
/// asserts the gate lets the caller through rather than asserting the move.
/// </summary>
public class RescheduleDelegationTests
{
    private const int DoctorOneUserId = 3;  // seeded doctor@clinicnow.test -> Doctor 1
    private const int StaffUserId = 2;      // seeded staff account

    private static AppointmentService BuildService(ClinicNowContext context, int userId, params string[] roles) =>
        new(
            context,
            TestContextFactory.CreateMapper(),
            new ServiceCollection().BuildServiceProvider(),
            TestContextFactory.CreateHttpContextAccessor(userId, roles),
            new ThrowingNotificationService(),
            new ThrowingEmailPublisher(),
            new ThrowingPaymentService());

    private static async Task<ClinicNowContext> SeedAppointmentForDoctorOneAsync()
    {
        var context = TestContextFactory.CreateContext();
        context.Appointments.Add(new Appointment
        {
            Id = 7001,
            PatientId = 1,
            DoctorId = 1,
            MedicalServiceId = 1,
            LocationId = 1,
            Status = AppointmentStatus.Pending,
            StartUtc = DateTime.UtcNow.AddDays(10),
            EndUtc = DateTime.UtcNow.AddDays(10).AddMinutes(30),
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return context;
    }

    [Fact]
    public async Task RescheduleAsync_AsDoctor_OntoAnotherDoctor_IsForbidden()
    {
        using var context = await SeedAppointmentForDoctorOneAsync();
        var service = BuildService(context, DoctorOneUserId, Roles.Doctor);

        var request = new AppointmentRescheduleRequest
        {
            DoctorId = 2, // seeded Doctor 2 - a colleague who never agreed to this
            StartUtc = DateTime.UtcNow.AddDays(11)
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => service.RescheduleAsync(7001, request));

        // Matched on the half of the message unique to this gate. The existing
        // ownership check nearby also says "drugog doktora", so asserting that
        // alone would pass even if this rejection never ran.
        Assert.Contains("obratite se osoblju klinike", ex.Message);
    }

    [Fact]
    public async Task RescheduleAsync_AsDoctor_OntoAnotherDoctor_LeavesTheAppointmentUntouched()
    {
        using var context = await SeedAppointmentForDoctorOneAsync();
        var service = BuildService(context, DoctorOneUserId, Roles.Doctor);

        var request = new AppointmentRescheduleRequest { DoctorId = 2, StartUtc = DateTime.UtcNow.AddDays(11) };
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RescheduleAsync(7001, request));

        // The gate has to run before anything is written, not after - otherwise
        // the rejection would still leave a half-delegated appointment behind.
        var stored = await context.Appointments.FindAsync(7001);
        Assert.Equal(1, stored!.DoctorId);
        Assert.Equal(AppointmentStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task RescheduleAsync_AsDoctor_KeepingTheSameDoctor_PassesTheDelegationGate()
    {
        using var context = await SeedAppointmentForDoctorOneAsync();
        var service = BuildService(context, DoctorOneUserId, Roles.Doctor);

        var request = new AppointmentRescheduleRequest { DoctorId = 1, StartUtc = DateTime.UtcNow.AddDays(11) };

        // It still fails - InMemory cannot open the Serializable transaction the
        // state machine needs - but it must not fail as a *permission* problem.
        // That distinction is the whole point: the gate must block delegation
        // without blocking an ordinary move.
        var ex = await Record.ExceptionAsync(() => service.RescheduleAsync(7001, request));
        Assert.NotNull(ex); // guards against a vacuous pass if this ever stops throwing
        Assert.IsNotType<ForbiddenException>(ex);
    }

    [Fact]
    public async Task RescheduleAsync_AsStaff_OntoAnotherDoctor_PassesTheDelegationGate()
    {
        using var context = await SeedAppointmentForDoctorOneAsync();
        var service = BuildService(context, StaffUserId, Roles.Staff);

        var request = new AppointmentRescheduleRequest { DoctorId = 2, StartUtc = DateTime.UtcNow.AddDays(11) };

        // Reassignment stays a staff power - the restriction is on doctors
        // handing their own work to each other, not on the clinic scheduling it.
        var ex = await Record.ExceptionAsync(() => service.RescheduleAsync(7001, request));
        Assert.NotNull(ex); // guards against a vacuous pass if this ever stops throwing
        Assert.IsNotType<ForbiddenException>(ex);
    }
}
