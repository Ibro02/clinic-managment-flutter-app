namespace ClinicNow.Model.Requests;

/// <summary>
/// Creates a specialist referral during an examination. No client-facing
/// <c>PatientId</c>/<c>ReferringDoctorId</c> - the service derives both from
/// the <see cref="SourceAppointmentId"/> server-side, matching
/// <c>LabFindingInsertRequest</c>'s reasoning.
/// </summary>
public class ReferralInsertRequest
{
    public int SourceAppointmentId { get; set; }

    public int TargetSpecializationId { get; set; }

    /// <summary>
    /// Optional: the specific specialist to refer the patient to. Must hold
    /// <see cref="TargetSpecializationId"/> - validated server-side. Omit to
    /// leave the choice of doctor to the patient.
    /// </summary>
    public int? TargetDoctorId { get; set; }

    /// <summary>The diagnosis/reason for the referral.</summary>
    public string Reason { get; set; } = string.Empty;
}
