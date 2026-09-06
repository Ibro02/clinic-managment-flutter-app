using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Payments;

/// <summary>
/// Bespoke, like <c>IAppointmentService</c> - ownership resolution (which
/// Patient row belongs to this JWT) needs an async lookup no generic
/// <c>ICRUDService</c> shape fits.
/// </summary>
public interface IPaymentService
{
    /// <summary>Starts a payment: creates the PayPal order and a Pending Payment row. `AppointmentId` is validated to belong to the caller.</summary>
    Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures a previously-created payment. Idempotent - already-Paid just returns the current state.</summary>
    Task<PaymentDto> CaptureAsync(int paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a still-Pending attempt the payer backed out of, so the next
    /// "Plati" isn't refused as a duplicate (review item C12). Idempotent on an
    /// already-cancelled row; rejects anything already settled.
    /// </summary>
    Task<PaymentDto> AbandonAsync(int paymentId, CancellationToken cancellationToken = default);

    /// <summary>Manual staff/admin refund, full or partial.</summary>
    Task<PaymentDto> RefundAsync(int paymentId, PaymentRefundRequest request, CancellationToken cancellationToken = default);

    Task<PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The automatic-refund half of design doc §4 item 4 - called from
    /// <c>AppointmentService.CancelAsync</c> after a cancellation succeeds.
    /// Never throws: a PayPal failure here is logged, not propagated, since
    /// the appointment cancellation itself must not be rolled back over a
    /// refund that can be retried manually by staff.
    /// </summary>
    Task RefundForCancelledAppointmentAsync(int appointmentId, int actingUserId, CancellationToken cancellationToken = default);
}
