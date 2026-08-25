namespace ClinicNow.Model.Dto;

/// <summary>
/// List/detail shape - never carries the raw file bytes (rulebook Part II §D:
/// list DTOs exclude heavy blobs). The actual file is served from a dedicated
/// download endpoint (<see cref="DownloadUrl"/>).
/// </summary>
public class MedicalDocumentDto
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Description { get; set; }

    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;
}
