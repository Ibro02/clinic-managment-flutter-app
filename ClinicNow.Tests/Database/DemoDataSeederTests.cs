using ClinicNow.Model.Common;
using ClinicNow.Services.Database;
using ClinicNow.Services.Security;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNow.Tests.Database;

/// <summary>
/// The seeder is what actually fixes the stale-demo-data bug (rulebook §3.1):
/// every migration <c>HasData</c> row is pinned to a fixed August 2026 date,
/// so on a clean database reviewed after that date, the dashboard/weekly
/// trend/mobile "Predstojeći" list are all empty. These tests exercise the
/// two properties that matter - it actually anchors rows to "today" (not a
/// hand-typed literal), and re-running it (a container restart) never
/// duplicates data.
/// </summary>
public class DemoDataSeederTests
{
    private static DemoDataSeeder BuildSeeder(ClinicNowContext context) =>
        new(context, new PasswordHasher(), NullLogger<DemoDataSeeder>.Instance);

    /// <summary>
    /// Deliberately does not assert an appointment dated "today" on the
    /// calendar - the actual invariant the dashboard needs is "something
    /// starts in the next 24h" (asserted precisely by
    /// <see cref="SeedAsync_CreatesAtLeastTwoAppointmentsInTheNext24Hours"/>),
    /// and a 24h window measured from "now" routinely spans into tomorrow's
    /// calendar date once it's past a doctor's last working hour today. Pinning
    /// the guarantee to "today" specifically was the same category of bug as
    /// K1 (a calendar-date guarantee that can already be stale by the time a
    /// reviewer opens the app), just scoped to a single row instead of the
    /// whole future half.
    /// </summary>
    [Fact]
    public async Task SeedAsync_CreatesANonCancelledAppointment()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var hasNonCancelledAppointment = await context.Appointments
            .AnyAsync(a => a.Status != AppointmentStatus.Cancelled);
        Assert.True(hasNonCancelledAppointment);
    }

    [Fact]
    public async Task SeedAsync_ProducesADemoableAmountOfData()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        // +2 existing HasData patients/doctors.
        Assert.InRange(await context.Patients.CountAsync(), 9, 9);
        Assert.InRange(await context.Doctors.CountAsync(), 5, 5);
        Assert.InRange(await context.Appointments.CountAsync(), 40, 60);
        Assert.InRange(await context.LabFindings.CountAsync(), 8, 9);
        Assert.InRange(await context.Referrals.CountAsync(), 3, 4);
        Assert.True(await context.MedicalRecordEntries.CountAsync() >= 10);

        // At least 3 distinct patients need real recommender history: the
        // one seeded via HasData (UserId 4) plus the two new ones this
        // seeder adds (UserId 110/111).
        var distinctUsersWithInteractions = await context.RecommenderInteractions
            .Select(i => i.UserId)
            .Distinct()
            .CountAsync();
        Assert.True(distinctUsersWithInteractions >= 3);
    }

    /// <summary>
    /// The actual regression this seeder shipped with (caught in the pre-
    /// submission review, not by any prior test): a single shared target
    /// walking chronologically from today-60 exhausted itself on past
    /// appointments before the loop ever reached "today", so the future half
    /// - including every <see cref="AppointmentStatus.Pending"/> row - was
    /// silently empty on every real review date. <see cref="SeedAsync_ProducesADemoableAmountOfData"/>
    /// only asserted a combined total, which stayed "in range" throughout.
    /// </summary>
    [Fact]
    public async Task SeedAsync_HasAppointmentsInTheFuture()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var nowUtc = DateTime.UtcNow;
        var futureCount = await context.Appointments.CountAsync(a => a.StartUtc > nowUtc);
        Assert.True(futureCount >= 15, $"Expected at least 15 future appointments, found {futureCount}.");
    }

    /// <summary>
    /// Feeds the dashboard's "Sljedeći termini" table, which queries a 24h
    /// window (<c>dashboard_screen.dart</c>'s <c>_upcomingWindow</c>) - a
    /// guarantee only of "some appointment today" is not equivalent, since a
    /// same-day slot at a fixed local time can already be in the past by the
    /// time a reviewer opens the app later in the day.
    /// </summary>
    [Fact]
    public async Task SeedAsync_CreatesAtLeastTwoAppointmentsInTheNext24Hours()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var nowUtc = DateTime.UtcNow;
        var windowEndUtc = nowUtc.AddHours(24);
        var within24h = await context.Appointments.CountAsync(a => a.StartUtc >= nowUtc && a.StartUtc < windowEndUtc);
        Assert.True(within24h >= 2, $"Expected at least 2 appointments in the next 24h, found {within24h}.");
    }

    /// <summary>
    /// Without a future <see cref="AppointmentStatus.Pending"/> row, staff has
    /// nothing to demonstrate the Pending → Confirmed transition on - every
    /// Pending row from a fixed HasData date is already in the past, and
    /// <see cref="ClinicNow.Services.Appointments.AppointmentStateMachine.ScheduledAppointmentState.ConfirmAsync"/>
    /// rejects confirming an appointment whose time has already passed.
    /// </summary>
    [Fact]
    public async Task SeedAsync_CreatesPendingAppointmentsInTheFuture()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var nowUtc = DateTime.UtcNow;
        var futurePending = await context.Appointments
            .CountAsync(a => a.Status == AppointmentStatus.Pending && a.StartUtc > nowUtc);
        Assert.True(futurePending >= 3, $"Expected at least 3 future Pending appointments, found {futurePending}.");
    }

    /// <summary>
    /// Patient 1 (<c>PatientConfiguration</c>'s <c>HasData</c> row) is the
    /// account behind the mobile demo login <c>patient@clinicnow.test</c> -
    /// reschedule/cancel/pay all need a future appointment to act on, and the
    /// 48h cancellation cutoff specifically needs one further out than that
    /// to demonstrate cancelling at all.
    /// </summary>
    [Fact]
    public async Task SeedAsync_GivesTheMobileDemoPatientAFutureAppointmentBeyondTheCancellationCutoff()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var cutoffUtc = DateTime.UtcNow.AddHours(48);
        var hasAppointmentBeyondCutoff = await context.Appointments
            .AnyAsync(a => a.PatientId == 1 && a.StartUtc > cutoffUtc);
        Assert.True(hasAppointmentBeyondCutoff, "Expected patient 1 to have a future appointment more than 48h out.");
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);
        var countAfterFirstRun = await context.Appointments.CountAsync();

        await seeder.SeedAsync(CancellationToken.None);
        var countAfterSecondRun = await context.Appointments.CountAsync();

        Assert.Equal(countAfterFirstRun, countAfterSecondRun);
    }

    /// <summary>
    /// A real bug this exact seeder shipped with: the new doctor/patient
    /// accounts authenticated fine (login only needs a password hash) but
    /// carried no role at all, since nothing ever wrote their UserRole rows -
    /// every role-gated action they tried then failed in whatever way an
    /// absent role happens to be treated, instead of a clean "wrong role".
    /// </summary>
    [Fact]
    public async Task SeedAsync_AssignsARoleToEveryNewAccount()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        foreach (var doctorUserId in new[] { 100, 101, 102 })
        {
            var roleIds = await context.UserRoles.Where(ur => ur.UserId == doctorUserId).Select(ur => ur.RoleId).ToListAsync();
            Assert.Equal([3], roleIds); // Doctor
        }

        foreach (var patientUserId in new[] { 110, 111 })
        {
            var roleIds = await context.UserRoles.Where(ur => ur.UserId == patientUserId).Select(ur => ur.RoleId).ToListAsync();
            Assert.Equal([4], roleIds); // Patient
        }
    }

    [Fact]
    public async Task SeedAsync_HashesNewPasswordsWithTheSamePasswordHasherAsHasData()
    {
        using var context = TestContextFactory.CreateContext();
        var hasher = new PasswordHasher();
        var seeder = new DemoDataSeeder(context, hasher, NullLogger<DemoDataSeeder>.Instance);

        await seeder.SeedAsync(CancellationToken.None);

        var newDoctorUser = await context.Users.SingleAsync(u => u.Id == 100);
        Assert.True(hasher.Verify("test", newDoctorUser.PasswordHash));
    }
}
