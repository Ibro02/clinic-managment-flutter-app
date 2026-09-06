namespace ClinicNow.Model.Dto;

/// <summary>
/// One payment attempt on one appointment (design doc §3). `ApproveUrl` is
/// only ever populated by `PaymentService.CreateAsync`'s response - every
/// other response leaves it null, since there's nothing left to approve.
/// </summary>
public class PaymentDto
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    /// <summary>What was ordered, priced server-side.</summary>
    public decimal AmountEur { get; set; }

    /// <summary>
    /// What PayPal reported it actually captured (review item C13a); null until
    /// captured. Equal to <see cref="AmountEur"/> on every healthy payment - the
    /// two differing is precisely the condition
    /// <see cref="Common.PaymentStatus.RequiresReconciliation"/> flags.
    /// </summary>
    public decimal? CapturedAmountEur { get; set; }

    public Common.PaymentStatus Status { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public decimal RefundedAmountEur { get; set; }

    public decimal RemainingRefundableEur { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    /// <summary>The PayPal-hosted approval page URL - populated only on creation.</summary>
    public string? ApproveUrl { get; set; }
}
