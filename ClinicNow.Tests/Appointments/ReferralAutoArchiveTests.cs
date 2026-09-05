using ClinicNow.Model.Common;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// Follow-up to review item C5, requested directly by Ibrahim: once the
/// appointment a referral was booked "from" (<c>Referral.ResultingAppointmentId</c>,
/// set by <c>AppointmentService.ScheduleAsync</c>) reaches a terminal state
/// (Completed or Cancelled), the referral has served its purpose and is
/// archived automatically - soft-deleted, never physically removed, so it
/// still shows up in the "Arhiva" view both Flutter clients render.
///
/// Reuses seeded rows (Patient 1, Doctor 1, Specialization 1, Appointment 1,
/// User 3) rather than constructing a fresh FK graph - the fixture only needs
/// a Referral row and an in-memory (not itself persisted) Appointment whose
/// Id matches its ResultingAppointmentId, since ArchiveResultingReferralIfAnyAsync
/// only ever queries the Referrals table, never dereferences the appointment's
/// own navigations - same construction style as RescheduleStateTests.
/// </summary>
public class ReferralAutoArchiveTests
{
    private static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

    private static async Task<(ClinicNow.Services.Database.ClinicNowContext Context, Referral Referral)> SeedReferralAsync(int resultingAppointmentId)
    {
        var context = TestContextFactory.CreateContext();
        var referral = new Referral
        {
            PatientId = 1,
            ReferringDoctorId = 1,
            SourceAppointmentId = 1,
            TargetSpecializationId = 1,
            Reason = "Test",
            CreatedByUserId = 3,
            CreatedAtUtc = DateTime.UtcNow,
            ResultingAppointmentId = resultingAppointmentId
        };
        context.Referrals.Add(referral);
        await context.SaveChangesAsync();
        return (context, referral);
    }

    [Fact]
    public async Task CompleteAsync_ArchivesTheReferralItResultedFrom()
    {
        var (context, referral) = await SeedReferralAsync(resultingAppointmentId: 501);
        var state = new ConfirmedAppointmentState(context, EmptyProvider);
        var appointment = new Appointment { Id = 501, Status = AppointmentStatus.Confirmed, StartUtc = DateTime.UtcNow.AddDays(-1) };

        await state.CompleteAsync(appointment, actingUserId: 1, CancellationToken.None);

        var archived = await context.Referrals.IgnoreQueryFilters().SingleAsync(r => r.Id == referral.Id);
        Assert.True(archived.IsDeleted);
        Assert.NotNull(archived.DeletedAtUtc);
    }

    [Fact]
    public async Task CancelAsync_ArchivesTheReferralItResultedFrom()
    {
        var (context, referral) = await SeedReferralAsync(resultingAppointmentId: 502);
        var state = new ScheduledAppointmentState(context, EmptyProvider);
        var appointment = new Appointment { Id = 502, Status = AppointmentStatus.Pending, StartUtc = DateTime.UtcNow.AddDays(10) };

        await state.CancelAsync(appointment, actingUserId: 1, reason: "Pacijent otkazao.", enforceCutoff: false, CancellationToken.None);

        var archived = await context.Referrals.IgnoreQueryFilters().SingleAsync(r => r.Id == referral.Id);
        Assert.True(archived.IsDeleted);
    }

    [Fact]
    public async Task CompleteAsync_WithNoResultingReferral_DoesNotThrow()
    {
        var context = TestContextFactory.CreateContext();
        var state = new ConfirmedAppointmentState(context, EmptyProvider);
        var appointment = new Appointment { Id = 999_999, Status = AppointmentStatus.Confirmed, StartUtc = DateTime.UtcNow.AddDays(-1) };

        await state.CompleteAsync(appointment, actingUserId: 1, CancellationToken.None);
        // No assertion beyond "did not throw" - the point is that archiving
        // is a no-op, not a hard dependency, when nothing references this appointment.
    }
}
