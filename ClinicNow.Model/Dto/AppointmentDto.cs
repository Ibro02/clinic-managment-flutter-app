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
}
