namespace ClinicNow.Model.Dto;

public class DiagnosisDto
{
    public int Id { get; set; }

    /// <summary>ICD-10 code, e.g. "J06.9".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int? SuggestedSpecializationId { get; set; }

    /// <summary>Resolved name, never the raw id - rulebook §6 forbids showing database ids in the UI.</summary>
    public string? SuggestedSpecializationName { get; set; }

    /// <summary>"J06.9 - Akutna infekcija gornjih disajnih puteva" - what dropdowns and tables render.</summary>
    public string DisplayName => $"{Code} - {Name}";
}
