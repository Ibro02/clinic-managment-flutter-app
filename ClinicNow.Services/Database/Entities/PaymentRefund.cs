namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One refund (automatic, on cancellation - or manual, staff/admin-triggered)
/// against a <see cref="Payment"/>. <see cref="Payment.Status"/> is always
/// derived from the sum of a payment's refunds vs. its <c>AmountEur</c> -
/// never a separately-maintained flag that could drift from the real total
/// (design doc §3).
/// </summary>
public class PaymentRefund
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    public Payment Payment { get; set; } = null!;

    public decimal AmountEur { get; set; }

    public string PayPalRefundId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int RefundedByUserId { get; set; }

    public User RefundedByUser { get; set; } = null!;

    public DateTime RefundedAtUtc { get; set; }
}
