using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.People;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.People;

/// <summary>
/// Review item C3: deleting a patient is archiving (soft-delete), not a hard
/// removal - it must deactivate the linked login (the reviewer's actual
/// defect: a "deleted" patient's account still passed login), while no longer
/// blanket-blocking archival just because the patient has *any* appointment
/// history. Only a future (Pending/Confirmed) appointment should still block
/// it - the same "active vs. resolved" distinction C10 already applies to
/// working-hours edits.
///
/// Uses dedicated patients created per test rather than the seeded Patient
/// 1/2 - both of those already carry seeded Pending/Confirmed appointments
/// (HasData), which would make every scenario here collide with fixture data
/// instead of testing what each test actually sets up.
/// </summary>
public class PatientServiceTests
{
    /// <summary>
    /// The accessor is only consulted by `GetOwnAsync` (review item C19), which
    /// none of these archive/restore tests call - it is here because the service
    /// resolves the caller from the token rather than from a parameter.
    /// </summary>
    private static PatientService NewService(ClinicNow.Services.Database.ClinicNowContext context) =>
        new(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(1));

    private static Patient NewPatient(int id, int? userId = null) => new()
    {
        Id = id,
        UserId = userId,
        FirstName = "Test",
        LastName = $"Patient{id}",
        Gender = Gender.Female,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static Appointment AppointmentFor(int patientId, AppointmentStatus status, TimeSpan offsetFromNow) => new()
    {
        PatientId = patientId,
        DoctorId = 1,
        MedicalServiceId = 1,
        LocationId = 1,
        StartUtc = DateTime.UtcNow + offsetFromNow,
        EndUtc = DateTime.UtcNow + offsetFromNow + TimeSpan.FromMinutes(30),
        Status = status,
        CreatedByUserId = 1,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static async Task<ClinicNowContext> ContextWithPatientAsync(Patient patient, params Appointment[] appointments)
    {
        var context = TestContextFactory.CreateContext();
        context.Patients.Add(patient);
        context.Appointments.AddRange(appointments);
        await context.SaveChangesAsync();
        return context;
    }

    [Fact]
    public async Task DeleteAsync_PatientWithNoAppointments_Succeeds()
    {
        // No linked User (UserId: null) - a walk-in patient staff created with
        // no login must not throw looking one up.
        await using var context = await ContextWithPatientAsync(NewPatient(500));
        var service = NewService(context);

        await service.DeleteAsync(500);

        var patient = await context.Patients.IgnoreQueryFilters().SingleAsync(p => p.Id == 500);
        Assert.True(patient.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_PatientWithOnlyResolvedAppointments_Succeeds()
    {
        await using var context = await ContextWithPatientAsync(
            NewPatient(501),
            AppointmentFor(501, AppointmentStatus.Completed, -TimeSpan.FromDays(10)),
            AppointmentFor(501, AppointmentStatus.Cancelled, -TimeSpan.FromDays(5)));
        var service = NewService(context);

        await service.DeleteAsync(501);

        var patient = await context.Patients.IgnoreQueryFilters().SingleAsync(p => p.Id == 501);
        Assert.True(patient.IsDeleted);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public async Task DeleteAsync_PatientWithAnActiveAppointment_Throws(AppointmentStatus activeStatus)
    {
        await using var context = await ContextWithPatientAsync(
            NewPatient(502),
            AppointmentFor(502, activeStatus, TimeSpan.FromDays(3)));
        var service = NewService(context);

        await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(502));

        var patient = await context.Patients.SingleAsync(p => p.Id == 502);
        Assert.False(patient.IsDeleted);
    }

    /// <summary>
    /// The actual reviewer-reported defect: archiving a patient must deactivate
    /// the linked login, or it still passes UserService.LoginAsync afterwards.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_DeactivatesTheLinkedUserAccount()
    {
        await using var context = TestContextFactory.CreateContext();
        var user = new User
        {
            Id = 500, Email = "walkin-test-patient@clinicnow.test", PasswordHash = "x",
            FirstName = "Walkin", LastName = "Test", IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        context.Patients.Add(NewPatient(503, userId: 500));
        await context.SaveChangesAsync();
        var service = NewService(context);

        await service.DeleteAsync(503);

        var reloadedUser = await context.Users.SingleAsync(u => u.Id == 500);
        Assert.False(reloadedUser.IsActive);
    }

    /// <summary>
    /// Feature requested by Ibrahim after C3 landed: Administrator/Staff need a
    /// way to see archived patients and undo an archive. <see cref="PatientService.RestoreAsync"/>
    /// is the exact reverse of BeforeDeleteAsync's archiving.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_ArchivedPatient_UnarchivesIt()
    {
        var patient = NewPatient(504);
        patient.IsDeleted = true;
        patient.DeletedAtUtc = DateTime.UtcNow.AddDays(-1);
        await using var context = await ContextWithPatientAsync(patient);
        var service = NewService(context);

        await service.RestoreAsync(504);

        var reloaded = await context.Patients.SingleAsync(p => p.Id == 504);
        Assert.False(reloaded.IsDeleted);
        Assert.Null(reloaded.DeletedAtUtc);
    }

    [Fact]
    public async Task RestoreAsync_ReactivatesTheLinkedUserAccount()
    {
        await using var context = TestContextFactory.CreateContext();
        var user = new User
        {
            Id = 501, Email = "restored-test-patient@clinicnow.test", PasswordHash = "x",
            FirstName = "Restored", LastName = "Test", IsActive = false, CreatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        var patient = NewPatient(505, userId: 501);
        patient.IsDeleted = true;
        patient.DeletedAtUtc = DateTime.UtcNow.AddDays(-1);
        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        var service = NewService(context);

        await service.RestoreAsync(505);

        var reloadedUser = await context.Users.SingleAsync(u => u.Id == 501);
        Assert.True(reloadedUser.IsActive);
    }

    [Fact]
    public async Task RestoreAsync_PatientNotArchived_Throws()
    {
        await using var context = await ContextWithPatientAsync(NewPatient(506));
        var service = NewService(context);

        await Assert.ThrowsAsync<BusinessException>(() => service.RestoreAsync(506));
    }

    [Fact]
    public async Task RestoreAsync_UnknownId_Throws()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);

        await Assert.ThrowsAsync<NotFoundException>(() => service.RestoreAsync(999_999));
    }

    [Fact]
    public async Task GetPagedAsync_OnlyDeleted_ReturnsArchivedPatientsOnly()
    {
        var archived = NewPatient(507);
        archived.IsDeleted = true;
        archived.DeletedAtUtc = DateTime.UtcNow;
        await using var context = await ContextWithPatientAsync(archived);
        context.Patients.Add(NewPatient(508)); // active, not archived
        await context.SaveChangesAsync();
        var service = NewService(context);

        var defaultResult = await service.GetPagedAsync(new PatientSearchObject());
        Assert.DoesNotContain(defaultResult.ResultList, p => p.Id == 507);
        Assert.Contains(defaultResult.ResultList, p => p.Id == 508);

        var archivedResult = await service.GetPagedAsync(new PatientSearchObject { OnlyDeleted = true });
        Assert.Contains(archivedResult.ResultList, p => p.Id == 507);
        Assert.DoesNotContain(archivedResult.ResultList, p => p.Id == 508);
    }
}
