namespace ClinicNow.Model.Requests;

/// <summary>
/// Starts a payment for one appointment. There is deliberately no `AmountEur`
/// field - the server always computes it from the appointment's own
/// `MedicalService.Price` (design doc §2: never trust the client's amount).
/// </summary>
public class PaymentCreateRequest
{
    public int AppointmentId { get; set; }
}

/// <summary>A manual staff/admin refund - always requires a reason, same convention as `AppointmentCancelRequest`.</summary>
public class PaymentRefundRequest
{
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;
}
