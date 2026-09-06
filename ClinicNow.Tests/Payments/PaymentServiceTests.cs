using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Notifications;
using ClinicNow.Services.Payments;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNow.Tests.Payments;

/// <summary>
/// Review item C12: one appointment must never end up with two payments that
/// can both take money. Two halves are tested here, and the recording
/// <see cref="FakePayPalClient"/> is what makes the second half meaningful -
/// every rejection asserts PayPal was <em>never called</em>, because the whole
/// point of the item is that the guard sits ahead of the provider call rather
/// than at the DB write after the money already moved.
///
/// Seeded fixtures: Patient 1 is user 4; appointment 5 is theirs and carries no
/// seeded payment; appointment 1 is theirs and is already Paid (seed payment 1).
/// </summary>
public class PaymentServiceTests
{
    private const int PatientUserId = 4;
    private const int CleanAppointmentId = 5;
    private const int AlreadyPaidAppointmentId = 1;

    private static (PaymentService Service, ClinicNowContext Context, FakePayPalClient PayPal) Build(ClinicNowContext? existing = null)
    {
        var context = existing ?? TestContextFactory.CreateContext();
        var payPal = new FakePayPalClient();
        var service = new PaymentService(
            context,
            TestContextFactory.CreateMapper(),
            payPal,
            TestContextFactory.CreateHttpContextAccessor(PatientUserId, Roles.Patient),
            new NoOpNotificationService(),
            NullLogger<PaymentService>.Instance);

        return (service, context, payPal);
    }

    private static async Task<Payment> SeedAttemptAsync(
        ClinicNowContext context, int appointmentId, PaymentStatus status, DateTime createdAtUtc, string? captureId = null)
    {
        var payment = new Payment
        {
            AppointmentId = appointmentId,
            AmountEur = 20.45m,
            Status = status,
            PayPalOrderId = $"TEST-ORDER-{Guid.NewGuid():N}"[..24],
            PayPalCaptureId = captureId,
            CreatedAtUtc = createdAtUtc,
            PaidAtUtc = status == PaymentStatus.Paid ? createdAtUtc : null
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment;
    }

    // --- creation guard --------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithAnAttemptStillInPlay_IsRefusedWithoutCreatingASecondOrder()
    {
        var (service, context, payPal) = Build();
        await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-2));

        await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new PaymentCreateRequest { AppointmentId = CleanAppointmentId }));

        Assert.Equal(0, payPal.CreateOrderCalls);
    }

    [Fact]
    public async Task CreateAsync_WithOnlyAStaleAttempt_SupersedesItAndCreatesANewOrder()
    {
        var (service, context, payPal) = Build();
        // Older than the 15-minute window: the patient's app died mid-payment
        // and no abandon call ever arrived, so they must not be locked out.
        var stale = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-45));

        var created = await service.CreateAsync(new PaymentCreateRequest { AppointmentId = CleanAppointmentId });

        Assert.Equal(1, payPal.CreateOrderCalls);
        Assert.Equal(PaymentStatus.Pending, (PaymentStatus)created.Status);

        // The superseded attempt is retired rather than left capturable - a
        // client still holding its id must not be able to charge against it.
        var reloadedStale = await context.Payments.SingleAsync(p => p.Id == stale.Id);
        Assert.Equal(PaymentStatus.Cancelled, reloadedStale.Status);
    }

    [Fact]
    public async Task CreateAsync_WhenTheAppointmentIsAlreadyPaid_IsRefusedWithoutCreatingAnOrder()
    {
        var (service, _, payPal) = Build();

        await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new PaymentCreateRequest { AppointmentId = AlreadyPaidAppointmentId }));

        Assert.Equal(0, payPal.CreateOrderCalls);
    }

    // --- capture guard ---------------------------------------------------------

    [Fact]
    public async Task CaptureAsync_WhenAnotherAttemptAlreadySucceeded_RefusesBeforeCallingPayPal()
    {
        var (service, context, payPal) = Build();
        await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Paid, DateTime.UtcNow.AddMinutes(-10), captureId: "TEST-CAPTURE-1");
        var loser = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-5));

        await Assert.ThrowsAsync<BusinessException>(() => service.CaptureAsync(loser.Id));

        // The assertion that matters: no second charge was ever attempted.
        Assert.Equal(0, payPal.CaptureOrderCalls);

        // And the losing attempt is retired, so a later full refund of the
        // winner can't re-open it as a capturable order.
        var reloaded = await context.Payments.SingleAsync(p => p.Id == loser.Id);
        Assert.Equal(PaymentStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task CaptureAsync_OnAnAbandonedAttempt_IsRefusedRatherThanReportedAsSuccess()
    {
        var (service, context, payPal) = Build();
        var abandoned = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Cancelled, DateTime.UtcNow.AddMinutes(-5));

        await Assert.ThrowsAsync<BusinessException>(() => service.CaptureAsync(abandoned.Id));

        Assert.Equal(0, payPal.CaptureOrderCalls);
    }

    [Fact]
    public async Task CaptureAsync_WhenPayPalRefusesTheCapture_RetiresTheAttemptSoTheNextOneCanStart()
    {
        // Regression, reported from live sandbox testing: PayPal answered
        // UNPROCESSABLE_ENTITY/COMPLIANCE_VIOLATION on a capture, the attempt
        // was left Pending, and every retry was then refused as "already in
        // progress" - with no way to clear it, because the PayPal screen the
        // patient would have to finish was already closed.
        var (service, context, payPal) = Build();
        payPal.CaptureSucceeds = false;
        var attempt = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-1));

        await Assert.ThrowsAsync<BusinessException>(() => service.CaptureAsync(attempt.Id));

        var reloaded = await context.Payments.SingleAsync(p => p.Id == attempt.Id);
        Assert.Equal(PaymentStatus.Cancelled, reloaded.Status);

        // The point of retiring it: paying again works right away.
        payPal.CaptureSucceeds = true;
        var retry = await service.CreateAsync(new PaymentCreateRequest { AppointmentId = CleanAppointmentId });
        Assert.Equal(PaymentStatus.Pending, (PaymentStatus)retry.Status);
    }

    [Fact]
    public async Task CaptureAsync_OnAnAlreadyPaidAttempt_StaysIdempotent()
    {
        var (service, context, payPal) = Build();
        var paid = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Paid, DateTime.UtcNow.AddMinutes(-5), captureId: "TEST-CAPTURE-2");

        var dto = await service.CaptureAsync(paid.Id);

        Assert.Equal(PaymentStatus.Paid, (PaymentStatus)dto.Status);
        Assert.Equal(0, payPal.CaptureOrderCalls);
    }

    // --- capture reconciliation (C13a) -----------------------------------------

    [Fact]
    public async Task CaptureAsync_WhenPayPalCapturesTheOrderedAmount_RecordsItAndMarksPaid()
    {
        var (service, context, payPal) = Build();
        var attempt = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-1));
        payPal.CapturedAmount = attempt.AmountEur;

        var dto = await service.CaptureAsync(attempt.Id);

        Assert.Equal(PaymentStatus.Paid, (PaymentStatus)dto.Status);
        Assert.Equal(attempt.AmountEur, dto.CapturedAmountEur);
    }

    [Fact]
    public async Task CaptureAsync_WhenPayPalCapturesADifferentAmount_FlagsForReconciliationRatherThanPaid()
    {
        var (service, context, payPal) = Build();
        var attempt = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-1));
        payPal.CapturedAmount = attempt.AmountEur - 5.00m; // PayPal took less than was ordered

        var dto = await service.CaptureAsync(attempt.Id);

        // Never a clean success - that is the whole of C13a.
        Assert.Equal(PaymentStatus.RequiresReconciliation, (PaymentStatus)dto.Status);

        var reloaded = await context.Payments.SingleAsync(p => p.Id == attempt.Id);
        // The real number is kept, and the ordered one is *not* overwritten -
        // losing either would erase the evidence that they disagreed.
        Assert.Equal(attempt.AmountEur - 5.00m, reloaded.CapturedAmountEur);
        Assert.Equal(attempt.AmountEur, reloaded.AmountEur);
        Assert.NotNull(reloaded.PayPalCaptureId);
    }

    [Fact]
    public async Task CreateAsync_WhenAnEarlierCaptureNeedsReconciliation_StillRefusesASecondPayment()
    {
        // Money moved on that attempt, mismatch or not, so a second payment
        // would double-charge.
        var (service, context, payPal) = Build();
        await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.RequiresReconciliation, DateTime.UtcNow.AddMinutes(-30), captureId: "TEST-CAPTURE-MISMATCH");

        await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new PaymentCreateRequest { AppointmentId = CleanAppointmentId }));

        Assert.Equal(0, payPal.CreateOrderCalls);
    }

    [Fact]
    public async Task RefundAsync_OnAMismatchedCapture_IsCappedByWhatWasActuallyCaptured()
    {
        var (service, context, _) = Build();
        var payment = new Payment
        {
            AppointmentId = CleanAppointmentId,
            AmountEur = 20.45m,          // ordered
            CapturedAmountEur = 10.00m,  // what PayPal actually took
            Status = PaymentStatus.RequiresReconciliation,
            PayPalOrderId = "TEST-ORDER-MISMATCH",
            PayPalCaptureId = "TEST-CAPTURE-MISMATCH",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            PaidAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        // Refunding against the *ordered* amount would ask PayPal to return
        // more than it ever collected.
        await Assert.ThrowsAsync<ValidationException>(
            () => service.RefundAsync(payment.Id, new PaymentRefundRequest { Amount = 15.00m, Reason = "Test" }));

        var dto = await service.RefundAsync(payment.Id, new PaymentRefundRequest { Amount = 10.00m, Reason = "Termin otkazan." });

        // Everything captured is back with the patient, so there is nothing
        // left to reconcile.
        Assert.Equal(PaymentStatus.Refunded, (PaymentStatus)dto.Status);
    }

    // --- refund failure is durable (C14) ---------------------------------------

    [Fact]
    public async Task RefundForCancelledAppointmentAsync_WhenTheRefundFails_RecordsTheDebtAndStillDoesNotThrow()
    {
        // Before C14 this case existed only as a log line: the patient was told
        // "cancelled" while their money stayed with the clinic, and nothing
        // anywhere recorded that it was owed.
        var (service, context, payPal) = Build();
        payPal.RefundThrows = true;
        var payment = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Paid, DateTime.UtcNow.AddMinutes(-30), captureId: "TEST-CAPTURE-REFUND");

        // Must not throw - a cancellation that already succeeded cannot be undone
        // by a refund failure.
        await service.RefundForCancelledAppointmentAsync(CleanAppointmentId, actingUserId: 1);

        var reloaded = await context.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.NotNull(reloaded.RefundFailedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(reloaded.RefundFailureReason));
        // The money is still with the clinic, so the payment stays refundable.
        Assert.Equal(PaymentStatus.Paid, reloaded.Status);
    }

    [Fact]
    public async Task RefundAsync_AfterAFailedAutomaticRefund_ClearsTheRecordedDebt()
    {
        // The staff "Refund" action doubles as the retry - that is what makes
        // the recorded failure actionable rather than just visible.
        var (service, context, payPal) = Build();
        payPal.RefundThrows = true;
        var payment = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Paid, DateTime.UtcNow.AddMinutes(-30), captureId: "TEST-CAPTURE-RETRY");
        await service.RefundForCancelledAppointmentAsync(CleanAppointmentId, actingUserId: 1);
        Assert.NotNull((await context.Payments.SingleAsync(p => p.Id == payment.Id)).RefundFailedAtUtc);

        payPal.RefundThrows = false; // PayPal is healthy again; staff retry
        await service.RefundAsync(payment.Id, new PaymentRefundRequest { Amount = payment.AmountEur, Reason = "Termin otkazan." });

        var reloaded = await context.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.Null(reloaded.RefundFailedAtUtc);
        Assert.Null(reloaded.RefundFailureReason);
        Assert.Equal(PaymentStatus.Refunded, reloaded.Status);
    }

    // --- abandon ---------------------------------------------------------------

    [Fact]
    public async Task AbandonAsync_RetiresThePendingAttemptAndIsIdempotent()
    {
        var (service, context, _) = Build();
        var attempt = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-1));

        await service.AbandonAsync(attempt.Id);
        // Called twice on purpose - the client fires this from a "screen
        // closed" handler, which can plausibly run more than once.
        var second = await service.AbandonAsync(attempt.Id);

        Assert.Equal(PaymentStatus.Cancelled, (PaymentStatus)second.Status);
        var reloaded = await context.Payments.SingleAsync(p => p.Id == attempt.Id);
        Assert.Equal(PaymentStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task AbandonAsync_AfterTheAttemptWasAbandoned_LetsANewOneStartImmediately()
    {
        // The whole reason the abandon endpoint exists: without it this second
        // CreateAsync would be refused for the full 15-minute window.
        var (service, context, payPal) = Build();
        var attempt = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Pending, DateTime.UtcNow.AddMinutes(-1));

        await service.AbandonAsync(attempt.Id);
        var created = await service.CreateAsync(new PaymentCreateRequest { AppointmentId = CleanAppointmentId });

        Assert.Equal(1, payPal.CreateOrderCalls);
        Assert.Equal(PaymentStatus.Pending, (PaymentStatus)created.Status);
    }

    [Fact]
    public async Task AbandonAsync_OnASettledPayment_IsRefused()
    {
        var (service, context, _) = Build();
        var paid = await SeedAttemptAsync(context, CleanAppointmentId, PaymentStatus.Paid, DateTime.UtcNow.AddMinutes(-5), captureId: "TEST-CAPTURE-3");

        await Assert.ThrowsAsync<BusinessException>(() => service.AbandonAsync(paid.Id));
    }

    // --- fakes -----------------------------------------------------------------

    /// <summary>
    /// Records call counts rather than just stubbing returns: "PayPal was not
    /// called" is the actual assertion most of these tests are making.
    /// </summary>
    private sealed class FakePayPalClient : IPayPalClient
    {
        public int CreateOrderCalls { get; private set; }
        public int CaptureOrderCalls { get; private set; }

        /// <summary>False reproduces PayPal answering and refusing the capture (declined, expired, compliance hold).</summary>
        public bool CaptureSucceeds { get; set; } = true;

        /// <summary>What PayPal claims it captured. Set it away from the ordered amount to exercise C13a's mismatch path.</summary>
        public decimal CapturedAmount { get; set; } = 20.45m;

        public Task<(string OrderId, string ApproveUrl)> CreateOrderAsync(
            decimal amountEur, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            CreateOrderCalls++;
            return Task.FromResult(($"FAKE-ORDER-{CreateOrderCalls}", "https://paypal.test/approve"));
        }

        public Task<(string? CaptureId, decimal CapturedAmountEur, bool Success)> CaptureOrderAsync(
            string orderId, CancellationToken cancellationToken = default)
        {
            CaptureOrderCalls++;
            return CaptureSucceeds
                ? Task.FromResult<(string?, decimal, bool)>(($"FAKE-CAPTURE-{CaptureOrderCalls}", CapturedAmount, true))
                : Task.FromResult<(string?, decimal, bool)>((null, 0m, false));
        }

        /// <summary>True reproduces PayPal being unreachable or rejecting the refund.</summary>
        public bool RefundThrows { get; set; }

        public Task<string> RefundCaptureAsync(string captureId, decimal amountEur, string reason, CancellationToken cancellationToken = default) =>
            RefundThrows
                ? throw new BusinessException("Povrat sredstava trenutno nije moguć. Pokušajte ponovo kasnije.")
                : Task.FromResult("FAKE-REFUND");
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task<PagedResult<NotificationDto>> GetPagedAsync(ClinicNow.Model.SearchObjects.NotificationSearchObject search, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<NotificationDto>());

        public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MarkAllAsReadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CreateAsync(int userId, string title, string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
