namespace ClinicNow.Model.Requests;

/// <summary>
/// Enters a new lab finding for a specific appointment. No client-facing
/// <c>PatientId</c> - the service derives it from the <see cref="AppointmentId"/>
/// server-side, so a caller can never mismatch a finding onto the wrong
/// patient's record.
/// </summary>
public class LabFindingInsertRequest
{
    public int AppointmentId { get; set; }

    /// <summary>The finding/interpretation text (e.g. "Kompletna krvna slika - uredni parametri").</summary>
    public string Result { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>Client-declared MIME type - re-validated server-side against the file's actual magic bytes, never trusted alone.</summary>
    public string ContentType { get; set; } = string.Empty;
    public string FileBase64 { get; set; } = string.Empty;
}
