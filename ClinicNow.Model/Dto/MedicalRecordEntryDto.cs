namespace ClinicNow.Model.Dto;

public class MedicalRecordEntryDto
{
    public int Id { get; set; }
    public DateOnly EntryDate { get; set; }

    // --- Structured diagnosis (prijava §4.1: a coded reference, not free text) ---
    public int DiagnosisId { get; set; }
    public string DiagnosisCode { get; set; } = string.Empty;
    public string DiagnosisName { get; set; } = string.Empty;

    /// <summary>Which specialization normally treats this diagnosis - what lets a referral be started straight from the entry.</summary>
    public int? DiagnosisSpecializationId { get; set; }

    /// <summary>Optional free-text qualifier on top of the coded diagnosis.</summary>
    public string? DiagnosisNote { get; set; }

    /// <summary>"J06.9 - Akutna infekcija gornjih disajnih puteva" - what tables and dropdowns render, never the raw id.</summary>
    public string DiagnosisDisplayName => $"{DiagnosisCode} - {DiagnosisName}";

    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
