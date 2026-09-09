namespace ClinicNow.Model.Dto;

public class ReferralDto
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;

    public int ReferringDoctorId { get; set; }
    public string ReferringDoctorName { get; set; } = string.Empty;

    public int SourceAppointmentId { get; set; }
    public DateTime SourceAppointmentStartUtc { get; set; }

    public int TargetSpecializationId { get; set; }
    public string TargetSpecializationName { get; set; } = string.Empty;

    /// <summary>The specific specialist named on the referral, when there is one. Null means "any doctor with the target specialization".</summary>
    public int? TargetDoctorId { get; set; }

    /// <summary>Resolved name, never the raw id (rulebook §6). Null when no specialist was named.</summary>
    public string? TargetDoctorName { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>True once an appointment has been booked "from" this referral - the client hides "Zakaži termin" once this is true, and the server independently refuses a second booking against it either way.</summary>
    public bool IsUsed { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
