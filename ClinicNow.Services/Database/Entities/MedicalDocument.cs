namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A file (PDF/image) plus an optional finding note attached to a patient's
/// record (CLAUDE.md §6: "MedicalDocument/MedicalFinding"). Legally-retained
/// health data - soft-delete only, never a hard DELETE. Uploaded by staff/a
/// doctor, downloaded by staff/doctors/administrators for any patient, or by
/// the patient themselves for their own record only (ownership enforced in
/// <c>MedicalDocumentService</c>, never trusted from the client).
///
/// The file bytes are validated against both the declared MIME type and the
/// file's actual magic bytes before being accepted (rulebook Part II §F:
/// "MIME + magic bytes, ne samo ekstenzija") - see
/// <see cref="MedicalDocumentService"/>.
/// </summary>
public class MedicalDocument : ISoftDelete
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] FileData { get; set; } = [];
    public long FileSizeBytes { get; set; }

    /// <summary>Free-text clinical finding/note attached alongside the file (may be empty if it's just a scan).</summary>
    public string? Description { get; set; }

    public int UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
