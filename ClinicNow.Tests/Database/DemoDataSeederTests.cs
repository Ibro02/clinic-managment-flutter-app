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

    [Fact]
    public async Task SeedAsync_CreatesAppointmentForToday()
    {
        using var context = TestContextFactory.CreateContext();
        var seeder = BuildSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var today = DateOnly.FromDateTime(ClinicTimeZone.NowLocal);
        var hasAppointmentToday = await context.Appointments
            .AnyAsync(a => a.Status != AppointmentStatus.Cancelled);
        Assert.True(hasAppointmentToday);

        var appointments = await context.Appointments.ToListAsync();
        Assert.Contains(appointments, a => ClinicTimeZone.LocalDateOf(a.StartUtc) == today);
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
