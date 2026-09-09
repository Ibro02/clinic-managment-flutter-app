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

    /// <summary>Which test was performed - e.g. "Kompletna krvna slika (KKS) - hemoglobin".</summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>Measured value, its unit, and the laboratory's normal range - all optional (a descriptive finding has no single number).</summary>
    public string? Value { get; set; }
    public string? Unit { get; set; }
    public string? ReferenceRange { get; set; }

    /// <summary>The finding/interpretation text.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>The doctor's remark on the finding.</summary>
    public string? DoctorNote { get; set; }

    /// <summary>Attachment metadata - null when no document was attached.</summary>
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }

    /// <summary>Whether there is a document to download at all - drives the client's "Preuzmi" affordance.</summary>
    public bool HasFile { get; set; }

    public string EnteredByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Empty when <see cref="HasFile"/> is false - there is nothing to fetch.</summary>
    public string DownloadUrl { get; set; } = string.Empty;
}
