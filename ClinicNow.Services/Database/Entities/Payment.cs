using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One payment attempt on one <see cref="Appointment"/> - fully decoupled
/// from the appointment's own state machine (design doc §3/§9). Multiple
/// rows can exist per appointment from abandoned attempts; the only hard
/// invariant is at most one <see cref="PaymentStatus.Paid"/> row per
/// appointment, enforced both in <c>PaymentService</c> and by a DB filtered
/// unique index (see <c>PaymentConfiguration</c>).
/// </summary>
public class Payment
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    public decimal AmountEur { get; set; }

    public PaymentStatus Status { get; set; }

    public string PayPalOrderId { get; set; } = string.Empty;

    public string? PayPalCaptureId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public ICollection<PaymentItem> Items { get; set; } = [];

    public ICollection<PaymentRefund> Refunds { get; set; } = [];
}
