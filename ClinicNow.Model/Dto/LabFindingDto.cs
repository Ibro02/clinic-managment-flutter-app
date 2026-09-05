namespace ClinicNow.Model.Dto;

/// <summary>
/// List/detail shape - never carries the raw file bytes (rulebook Part II §D:
/// list DTOs exclude heavy blobs). The actual file is served from a dedicated
/// download endpoint (<see cref="DownloadUrl"/>), same pattern as
/// <see cref="MedicalDocumentDto"/>.
/// </summary>
public class LabFindingDto
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;

    public int AppointmentId { get; set; }
    public DateTime AppointmentStartUtc { get; set; }
    public string MedicalServiceName { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }

    public string EnteredByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;
}
