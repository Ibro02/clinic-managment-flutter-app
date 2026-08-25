namespace ClinicNow.Model.Requests;

/// <summary>A Doctor's append - concatenated onto the existing text (never replaces it), so prior content can never be lost/edited away by a Doctor.</summary>
public class MedicalRecordAppendNotesRequest
{
    public string? AllergiesToAppend { get; set; }
    public string? MedicalNotesToAppend { get; set; }
}

/// <summary>Administrator-only full replace of the header text fields (real CRUD, unlike the Doctor's append-only endpoint).</summary>
public class MedicalRecordUpdateNotesRequest
{
    public string? Allergies { get; set; }
    public string? MedicalNotes { get; set; }
}

/// <summary>Adds one row to the treatment history table. All three fields are required (rulebook: Date/Treatment/Description all mandatory).</summary>
public class MedicalRecordEntryInsertRequest
{
    public DateOnly EntryDate { get; set; }
    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Administrator-only edit of an existing entry (Doctors can only add rows, never edit/delete them).</summary>
public class MedicalRecordEntryUpdateRequest
{
    public DateOnly EntryDate { get; set; }
    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
