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
        NewService(userId, role, out _);

    private static ReferralService NewService(int userId, string role, out RecordingNotificationService notifications)
    {
        notifications = new RecordingNotificationService();
        return new ReferralService(
            TestContextFactory.CreateContext(),
            TestContextFactory.CreateMapper(),
            TestContextFactory.CreateHttpContextAccessor(userId, role),
            notifications);
    }

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

    /// <summary>
    /// The prijava states the patient "o njenom izdavanju dobija notifikaciju",
    /// and rulebook §7.2 requires a notification per relevant event - so this
    /// asserts the side effect, not just the returned DTO.
    /// </summary>
    [Fact]
    public async Task CreateAsync_NotifiesThePatient()
    {
        var service = NewService(3, Roles.Doctor, out var notifications);

        // Seeded Appointment 3 is Patient 1, whose login is User 4. (Patient 2,
        // used by the other tests here, is seeded without an account on
        // purpose - see CreateAsync_PatientWithoutAnAccountIsNotNotified.)
        await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 3,
            TargetSpecializationId = 4,
            Reason = "Sumnja na aritmiju."
        });

        var notification = Assert.Single(notifications.Created);
        Assert.Equal(4, notification.UserId);
        Assert.Equal("Nova uputnica", notification.Title);
        Assert.Contains("Kardiologija", notification.Text);
    }

    /// <summary>
    /// <c>Patient.UserId</c> is nullable - a staff-created patient has no
    /// account to notify, and issuing the referral must still succeed.
    /// </summary>
    [Fact]
    public async Task CreateAsync_PatientWithoutAnAccountIsNotNotified()
    {
        var service = NewService(3, Roles.Doctor, out var notifications);

        // Appointment 4 belongs to Patient 2, seeded with UserId = null.
        var dto = await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 4,
            Reason = "Sumnja na aritmiju."
        });

        Assert.Equal(2, dto.PatientId);
        Assert.Empty(notifications.Created);
    }

    /// <summary>
    /// The prijava's "a po potrebi i konkretnog specijalistu": naming one is
    /// optional, but the named doctor must actually hold the target
    /// specialization or the referral could never be redeemed.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WithTargetDoctor_StoresAndResolvesTheName()
    {
        var service = NewService(3, Roles.Doctor);

        // Doctor 2 holds Specialization 4 (Kardiologija) per the seed.
        var dto = await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 4,
            TargetDoctorId = 2,
            Reason = "Sumnja na aritmiju."
        });

        Assert.Equal(2, dto.TargetDoctorId);
        Assert.False(string.IsNullOrWhiteSpace(dto.TargetDoctorName));
    }

    [Fact]
    public async Task CreateAsync_TargetDoctorLacksTheSpecialization_Throws()
    {
        var service = NewService(3, Roles.Doctor);

        // Doctor 1 holds Specializations 1 and 2 - not 4 (Kardiologija).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 4,
            TargetDoctorId = 1,
            Reason = "Sumnja na aritmiju."
        }));

        Assert.Contains("targetDoctorId", ex.Errors.Keys);
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
        var service = new ReferralService(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(3, Roles.Doctor), new RecordingNotificationService());

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
    public async Task GetPagedAsync_FilteredBySearch_MatchesReasonOrSpecializationName()
    {
        var service = NewService(3, Roles.Doctor);
        await service.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4,
            TargetSpecializationId = 4, // Kardiologija
            Reason = "Sumnja na aritmiju, potrebna kardiološka evaluacija."
        });

        // Seeded Referral Id=1 ("Povišen krvni pritisak...", Kardiologija too)
        // also matches "kardio" - both hits are expected, not just the new one.
        var byReason = await service.GetPagedAsync(new ReferralSearchObject { Search = "aritmiju" });
        Assert.Single(byReason.ResultList);
        Assert.Contains("aritmiju", byReason.ResultList[0].Reason);

        var bySpecialization = await service.GetPagedAsync(new ReferralSearchObject { Search = "Kardiologija" });
        Assert.True(bySpecialization.ResultList.Count >= 2);
        Assert.All(bySpecialization.ResultList, r => Assert.Equal("Kardiologija", r.TargetSpecializationName));
    }

    [Fact]
    public async Task GetPagedAsync_PatientRole_OnlySeesOwnReferrals()
    {
        // Patient 1's User is Id=4 (Hana, per PatientConfiguration/AddIdentity seed).
        var context = TestContextFactory.CreateContext();
        var adminAccessor = TestContextFactory.CreateHttpContextAccessor(1, Roles.Administrator);
        var adminService = new ReferralService(context, TestContextFactory.CreateMapper(), adminAccessor, new RecordingNotificationService());
        await adminService.CreateAsync(new ReferralInsertRequest
        {
            SourceAppointmentId = 4, // Patient 2
            TargetSpecializationId = 2,
            Reason = "Tuđi nalaz"
        });

        var patientAccessor = TestContextFactory.CreateHttpContextAccessor(4, Roles.Patient);
        var patientService = new ReferralService(context, TestContextFactory.CreateMapper(), patientAccessor, new RecordingNotificationService());

        var result = await patientService.GetPagedAsync(new ReferralSearchObject { PatientId = 2 }); // attempted spoof

        Assert.All(result.ResultList, r => Assert.Equal(1, r.PatientId));
    }

    [Fact]
    public async Task GetPagedAsync_UsedReferral_ShowsUnderArchivedNotActive()
    {
        // Requested directly by Ibrahim: a used referral belongs in "Arhiva"
        // immediately, not only once its resulting appointment reaches a
        // terminal state - IsDeleted stays false here on purpose, to prove
        // ResultingAppointmentId alone is what the archived/active split now
        // keys off, independent of the soft-delete flag.
        var context = TestContextFactory.CreateContext();
        var created = await new ReferralService(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(3, Roles.Doctor), new RecordingNotificationService())
            .CreateAsync(new ReferralInsertRequest { SourceAppointmentId = 4, TargetSpecializationId = 2, Reason = "Nalaz" });

        var referral = await context.Referrals.SingleAsync(r => r.Id == created.Id);
        referral.ResultingAppointmentId = 999_999;
        await context.SaveChangesAsync();

        var service = new ReferralService(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(3, Roles.Doctor), new RecordingNotificationService());

        var active = await service.GetPagedAsync(new ReferralSearchObject { PatientId = 2, OnlyArchived = false });
        Assert.DoesNotContain(active.ResultList, r => r.Id == created.Id);

        var archived = await service.GetPagedAsync(new ReferralSearchObject { PatientId = 2, OnlyArchived = true });
        Assert.Contains(archived.ResultList, r => r.Id == created.Id);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        var context = TestContextFactory.CreateContext();
        var service = new ReferralService(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(1, Roles.Administrator), new RecordingNotificationService());
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
