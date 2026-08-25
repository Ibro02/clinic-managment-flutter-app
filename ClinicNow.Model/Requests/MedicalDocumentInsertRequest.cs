namespace ClinicNow.Model.Requests;

/// <summary>
/// Uploads a new file onto a patient's record. The file arrives Base64-encoded
/// in the JSON body (consistent with <c>NewsItemInsertRequest</c>'s image
/// field) rather than multipart/form-data, keeping every write endpoint in
/// this API on one consistent JSON contract.
/// </summary>
public class MedicalDocumentInsertRequest
{
    public int PatientId { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>Client-declared MIME type - re-validated server-side against the file's actual magic bytes, never trusted alone.</summary>
    public string ContentType { get; set; } = string.Empty;
    public string FileBase64 { get; set; } = string.Empty;
    public string? Description { get; set; }
}
