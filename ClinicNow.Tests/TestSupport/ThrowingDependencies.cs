using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Messaging;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Notifications;
using ClinicNow.Services.Payments;

namespace ClinicNow.Tests.TestSupport;

// AppointmentService takes notification, email and payment collaborators that
// several of its code paths never touch - the referral rejection gate (C5) and
// slot generation (C19) among them. These fakes make that explicit: if a change
// ever routes one of those paths through a side effect, the test fails loudly
// instead of silently sending mail. Shared here rather than duplicated per test
// class.

public sealed class ThrowingNotificationService : INotificationService
{
    private const string Message = "Not expected to be called on the code path under test.";

    public Task<PagedResult<NotificationDto>> GetPagedAsync(
        NotificationSearchObject search, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task MarkAllAsReadAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task CreateAsync(int userId, string title, string text, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);
}

public sealed class ThrowingEmailPublisher : IEmailPublisher
{
    public Task<bool> PublishAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not expected to be called on the code path under test.");
}

public sealed class ThrowingPaymentService : IPaymentService
{
    private const string Message = "Not expected to be called on the code path under test.";

    public Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<PaymentDto> CaptureAsync(int paymentId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<PaymentDto> AbandonAsync(int paymentId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<PaymentDto> RefundAsync(int paymentId, PaymentRefundRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task<PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public Task RefundForCancelledAppointmentAsync(int appointmentId, int actingUserId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);
}
