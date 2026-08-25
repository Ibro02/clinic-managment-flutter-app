namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A patient's single medical file ("medicinski karton") - exactly one per
/// patient (enforced by a unique index on <see cref="PatientId"/>, created
/// automatically alongside the <see cref="Patient"/> - see
/// <c>PatientService.AfterInsertAsync</c>).
///
/// <see cref="Allergies"/>/<see cref="MedicalNotes"/> are append-only for a
/// Doctor (<c>MedicalRecordService.AppendNotesAsync</c> concatenates, never
/// overwrites) - only an Administrator can fully replace them
/// (<c>MedicalRecordService.ReplaceNotesAsync</c>). The treatment history
/// itself lives in <see cref="Entries"/>, which is append-only for a Doctor
/// in the same way - new rows only, no edit/delete outside Administrator.
/// </summary>
public class MedicalRecord
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public string? Allergies { get; set; }
    public string? MedicalNotes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<MedicalRecordEntry> Entries { get; set; } = [];
}
