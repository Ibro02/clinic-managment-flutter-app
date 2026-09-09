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

    /// <summary>Which test was performed - required, it is what the finding is.</summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>Measured value, its unit, and the laboratory's normal range. All optional.</summary>
    public string? Value { get; set; }
    public string? Unit { get; set; }
    public string? ReferenceRange { get; set; }

    /// <summary>The finding/interpretation text (e.g. "Kompletna krvna slika - uredni parametri").</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>The doctor's own remark on the finding. Optional.</summary>
    public string? DoctorNote { get; set; }

    /// <summary>
    /// Optional attachment. Leave <see cref="FileBase64"/> empty to enter a
    /// finding with no document; supply it and both <see cref="FileName"/> and
    /// <see cref="ContentType"/> become required.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>Client-declared MIME type - re-validated server-side against the file's actual magic bytes, never trusted alone.</summary>
    public string? ContentType { get; set; }
    public string? FileBase64 { get; set; }
}
