namespace ClinicNow.Model.Dto;

public class AppointmentDto
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public string PatientName { get; set; } = string.Empty;

    public int DoctorId { get; set; }

    public string DoctorName { get; set; } = string.Empty;

    public int MedicalServiceId { get; set; }

    public string MedicalServiceName { get; set; } = string.Empty;

    public int LocationId { get; set; }

    public string LocationName { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public Common.AppointmentStatus Status { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public string? CancellationReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Legal next actions from the current status, per the state machine's
    /// <c>AllowedActions()</c> - lets the UI disable buttons that would fail
    /// server-side anyway, with a reason (rulebook Part II §K).
    /// </summary>
    public List<string> AllowedActions { get; set; } = [];

    /// <summary>True once a Payment on this appointment reached Paid or PartiallyRefunded - hides the "Pay" button, shows a "Plaćeno" badge (rulebook Part II §J).</summary>
    public bool IsPaid { get; set; }

    /// <summary>Display name of the current payment's status, or null if nothing beyond a Pending attempt exists yet.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>The current (non-Pending) payment's id, if any - lets the UI call the refund endpoint directly without a lookup.</summary>
    public int? PaymentId { get; set; }

    /// <summary>True when there is a Paid/PartiallyRefunded payment with a remaining refundable balance &gt; 0.</summary>
    public bool CanRefund { get; set; }
}
