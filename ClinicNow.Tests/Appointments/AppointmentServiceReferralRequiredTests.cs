using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Messaging;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Notifications;
using ClinicNow.Services.Payments;
using ClinicNow.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// New feature (not in the C1-C19 review list): a <c>MedicalService</c> can be
/// flagged <c>IsReferralRequired</c> so a patient can't self-book it without
/// first being referred - e.g. a surgical consultation. Seeded service 5
/// ("Kardiološki pregled", SpecializationId 4) carries the flag.
///
/// Both cases here throw in <c>AppointmentService.ScheduleAsync</c> before
/// <c>InitialAppointmentState.ScheduleAsync</c> ever opens its Serializable
/// transaction, so - like the C5 referral-reuse checks they sit next to -
/// they're safely testable against EF InMemory; a successful booking through
/// this gate is not (same split established for C1/C5), and is verified live
/// instead.
/// </summary>
public class AppointmentServiceReferralRequiredTests
{
    private const int PatientUserId = 4; // seeded patient@clinicnow.test -> Patient 1

    private static AppointmentService BuildService(ClinicNow.Services.Database.ClinicNowContext context)
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var httpContextAccessor = TestContextFactory.CreateHttpContextAccessor(PatientUserId, Roles.Patient);

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
    public async Task ScheduleAsync_ReferralRequiredService_WithNoReferral_Throws()
    {
        var context = TestContextFactory.CreateContext();
        var service = BuildService(context);

        var request = new AppointmentInsertRequest
        {
            // DoctorId is never reached - the gate throws before EnsureAvailableAsync
            // would validate doctor/service compatibility - so any seeded id works.
            DoctorId = 1,
            MedicalServiceId = 5, // IsReferralRequired = true, SpecializationId = 4
            StartUtc = DateTime.UtcNow.AddDays(1)
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ScheduleAsync(request));
        Assert.Contains("referralId", ex.Errors.Keys);
    }

    [Fact]
    public async Task ScheduleAsync_ReferralRequiredService_WithWrongSpecializationReferral_Throws()
    {
        var context = TestContextFactory.CreateContext();
        var referral = new Referral
        {
            PatientId = 1,
            ReferringDoctorId = 1,
            SourceAppointmentId = 1,
            TargetSpecializationId = 1, // not 4 - doesn't match the service's specialization
            Reason = "Test",
            CreatedByUserId = 3,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Referrals.Add(referral);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var request = new AppointmentInsertRequest
        {
            DoctorId = 1,
            MedicalServiceId = 5,
            StartUtc = DateTime.UtcNow.AddDays(1),
            ReferralId = referral.Id
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ScheduleAsync(request));
        Assert.Contains("referralId", ex.Errors.Keys);
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public Task<ClinicNow.Model.Common.PagedResult<ClinicNow.Model.Dto.NotificationDto>> GetPagedAsync(
            ClinicNow.Model.SearchObjects.NotificationSearchObject search, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task MarkAllAsReadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task CreateAsync(int userId, string title, string text, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");
    }

    private sealed class ThrowingEmailPublisher : IEmailPublisher
    {
        public Task<bool> PublishAsync(EmailMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");
    }

    private sealed class ThrowingPaymentService : IPaymentService
    {
        public Task<ClinicNow.Model.Dto.PaymentDto> CreateAsync(ClinicNow.Model.Requests.PaymentCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task<ClinicNow.Model.Dto.PaymentDto> CaptureAsync(int paymentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task<ClinicNow.Model.Dto.PaymentDto> AbandonAsync(int paymentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task<ClinicNow.Model.Dto.PaymentDto> RefundAsync(int paymentId, ClinicNow.Model.Requests.PaymentRefundRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task<ClinicNow.Model.Dto.PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");

        public Task RefundForCancelledAppointmentAsync(int appointmentId, int actingUserId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not expected to be called on the rejection path.");
    }
}
