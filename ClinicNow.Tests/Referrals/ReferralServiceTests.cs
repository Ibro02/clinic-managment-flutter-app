using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Referrals;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Referrals;

/// <summary>
/// Review item C5: a referral is tied to a patient (derived from the source
/// appointment, never a separately-trusted client input, same reasoning as
/// C4's LabFindingService) and a target specialization, never a specific
/// doctor - booking "with the appropriate specialist" is a later, separate
/// step.
///
/// Uses the seeded Appointments (Id 1-5) rather than hand-building the
/// Doctor/MedicalService/Location FK graph a fresh Appointment would need.
/// </summary>
public class ReferralServiceTests
{
    private static ReferralService NewService(int userId, string role) =>
        new(TestContextFactory.CreateContext(), TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(userId, role));

    [Fact]
    public async Task CreateAsync_DerivesPatientAndReferringDoctorFromAppointment()
    {
        var service = NewService(3, Roles.Doctor);

        // Seeded Appointment 4 is Patient 2 / Doctor 1.
        var dto = await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 4, // Kardiologija
            Reason = "Sumnja na aritmiju."
        });

        Assert.Equal(2, dto.PatientId);
        Assert.Equal(1, dto.ReferringDoctorId);
        Assert.Equal(4, dto.TargetSpecializationId);
        Assert.Equal("Kardiologija", dto.TargetSpecializationName);
    }

    [Fact]
    public async Task CreateAsync_MissingReason_Throws()
    {
        var service = NewService(3, Roles.Doctor);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 4,
            Reason = ""
        }));

        Assert.Contains("reason", ex.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_UnknownAppointment_Throws()
    {
        var service = NewService(3, Roles.Doctor);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 999_999,
            TargetSpecializationId = 4,
            Reason = "Nalaz"
        }));
    }

    [Fact]
    public async Task CreateAsync_UnknownSpecialization_Throws()
    {
        var service = NewService(3, Roles.Doctor);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 999_999,
            Reason = "Nalaz"
        }));
    }

    [Fact]
    public async Task GetPagedAsync_FilteredByPatientId_ReturnsOnlyThatPatientsReferrals()
    {
        var context = TestContextFactory.CreateContext();
        var service = new ReferralService(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(3, Roles.Doctor));

        await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4, // Patient 2
            TargetSpecializationId = 2,
            Reason = "Nalaz za pacijenta 2"
        });

        var result = await service.GetPagedAsync(new ReferralSearchObject { PatientId = 2 });

        Assert.Single(result.ResultList);
        Assert.Equal(2, result.ResultList[0].PatientId);
    }

    [Fact]
    public async Task GetPagedAsync_PatientRole_OnlySeesOwnReferrals()
    {
        // Patient 1's User is Id=4 (Hana, per PatientConfiguration/AddIdentity seed).
        var context = TestContextFactory.CreateContext();
        var adminAccessor = TestContextFactory.CreateHttpContextAccessor(1, Roles.Administrator);
        var adminService = new ReferralService(context, TestContextFactory.CreateMapper(), adminAccessor);
        await adminService.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4, // Patient 2
            TargetSpecializationId = 2,
            Reason = "Tuđi nalaz"
        });

        var patientAccessor = TestContextFactory.CreateHttpContextAccessor(4, Roles.Patient);
        var patientService = new ReferralService(context, TestContextFactory.CreateMapper(), patientAccessor);

        var result = await patientService.GetPagedAsync(new ReferralSearchObject { PatientId = 2 }); // attempted spoof

        Assert.All(result.ResultList, r => Assert.Equal(1, r.PatientId));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        var context = TestContextFactory.CreateContext();
        var service = new ReferralService(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(1, Roles.Administrator));
        var created = await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 2,
            Reason = "Nalaz"
        });

        await service.DeleteAsync(created.Id);

        var directQuery = await context.Referrals.Where(r => r.Id == created.Id).ToListAsync();
        Assert.Empty(directQuery); // global soft-delete filter excludes it

        var stillInDb = await context.Referrals.IgnoreQueryFilters().SingleAsync(r => r.Id == created.Id);
        Assert.True(stillInDb.IsDeleted);
    }
}
