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

    /// <summary>The actual finding/interpretation text (e.g. "Kompletna krvna slika - uredni parametri") - the structured content a plain file attachment doesn't carry.</summary>
    public string Result { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] FileData { get; set; } = [];
    public long FileSizeBytes { get; set; }

    public int EnteredByUserId { get; set; }
    public User EnteredByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
