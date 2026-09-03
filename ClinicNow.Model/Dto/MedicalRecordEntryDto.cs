namespace ClinicNow.Model.Dto;

public class MedicalRecordEntryDto
{
    public int Id { get; set; }
    public DateOnly EntryDate { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
