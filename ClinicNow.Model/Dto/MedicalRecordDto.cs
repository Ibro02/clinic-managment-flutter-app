using ClinicNow.Model.Common;

namespace ClinicNow.Model.Dto;

/// <summary>
/// The full medical file ("medicinski karton") - header (patient basic info,
/// denormalized so the UI never has to make a second call), allergies/notes,
/// and the full treatment history table. Always returned whole (rulebook Part
/// II §K: a real, browsable document, not a raw ID list) - there is no
/// separate paged listing of entries, since a single patient's history is
/// expected to stay reasonably sized.
/// </summary>
public class MedicalRecordDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }

    // --- Header: basic patient info (rulebook Part II §K: never show raw IDs -
    // denormalized here so the medical file view never needs a second call) ---
    public string PatientFirstName { get; set; } = string.Empty;
    public string PatientLastName { get; set; } = string.Empty;
    public int? PatientAge { get; set; }
    public Gender? PatientGender { get; set; }
    public string? PatientAddress { get; set; }
    public string? PatientEmail { get; set; }
    public string? PatientPhoneNumber { get; set; }

    // --- Allergies & medical notes ---
    public string? Allergies { get; set; }
    public string? MedicalNotes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // --- Treatment history table (Date / Treatment / Description) ---
    public List<MedicalRecordEntryDto> Entries { get; set; } = [];
}
