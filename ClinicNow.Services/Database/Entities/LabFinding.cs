using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A lab finding ("laboratorijski nalaz") - review item C4. Unlike a generic
/// <see cref="MedicalDocument"/>, this is deliberately tied to **both** the
/// patient and the specific <see cref="Appointment"/> it was produced for (the
/// prijava's requirement the reviewer names explicitly), with a structured
/// <see cref="Result"/> field rather than being just a file with an optional
/// note. Entered by a doctor or lab staff during/after an examination;
/// legally-retained health data - soft-delete only, never a hard DELETE.
///
/// <see cref="PatientId"/> is denormalized from <see cref="Appointment.PatientId"/>
/// at creation time (derived server-side in <c>LabFindingService.CreateAsync</c>,
/// never taken from the client) rather than requiring every query to join
/// through <see cref="Appointment"/> to find a patient's lab history.
/// </summary>
public class LabFinding : ISoftDelete
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    /// <summary>Optional - a soft-deleted (archived) patient's findings must stay readable, same reasoning as <c>MedicalDocument.Patient</c> (review item C3).</summary>
    public Patient? Patient { get; set; }

    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;

    /// <summary>Which test was performed, e.g. "Kompletna krvna slika (KKS)". Required - it is what the finding *is*.</summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>The measured value, e.g. "13.9". Optional: a descriptive finding (an imaging report, say) has no single number.</summary>
    public string? Value { get; set; }

    /// <summary>Unit of measure for <see cref="Value"/>, e.g. "g/dL".</summary>
    public string? Unit { get; set; }

    /// <summary>The laboratory's normal range for this test, e.g. "12.0 - 16.0", so the value can be read in context.</summary>
    public string? ReferenceRange { get; set; }

    /// <summary>The finding/interpretation text (e.g. "Kompletna krvna slika - uredni parametri") - what the numbers mean.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>The doctor's own remark on the finding - distinct from <see cref="Result"/>, which states the finding itself.</summary>
    public string? DoctorNote { get; set; }

    /// <summary>
    /// The attached document, if any. Optional throughout (prijava: "uz nalaz se
    /// <em>može</em> priložiti i dokument u PDF ili slikovnom formatu") - a finding
    /// entered straight from a lab report still stands on its structured fields
    /// alone. All four move together: either every one is set, or none is.
    /// </summary>
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public byte[]? FileData { get; set; }
    public long FileSizeBytes { get; set; }

    /// <summary>True when a document is attached and the download endpoint has something to serve. Derived, never a column.</summary>
    [NotMapped]
    public bool HasFile => FileData != null && FileData.Length > 0;

    /// <summary>
    /// SHA-256 of <see cref="FileData"/>, stored at upload time and served as the
    /// download's ETag. Kept as a column rather than hashed per request: the point
    /// of the ETag is to avoid sending the bytes, so reading them back to hash them
    /// would trade the bandwidth saving for exactly the I/O it was meant to avoid.
    /// Null only for rows written before this column existed.
    /// </summary>
    public string? ContentHash { get; set; }

    public int EnteredByUserId { get; set; }
    public User EnteredByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
