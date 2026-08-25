namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One row of the medical record's treatment history table (Date/Treatment/
/// Description - all three required). Added by a Doctor (or Administrator)
/// and never edited/deleted afterward except by an Administrator - see
/// <see cref="MedicalRecord"/>'s remarks.
/// </summary>
public class MedicalRecordEntry
{
    public int Id { get; set; }

    public int MedicalRecordId { get; set; }
    public MedicalRecord MedicalRecord { get; set; } = null!;

    public DateOnly EntryDate { get; set; }
    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
}
