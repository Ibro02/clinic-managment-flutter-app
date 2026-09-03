namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One row of the medical record's treatment history table (Date/Diagnosis/
/// Treatment/Description - all required). Added by a Doctor (or
/// Administrator) and never edited/deleted afterward except by an
/// Administrator - see <see cref="MedicalRecord"/>'s remarks.
///
/// <see cref="Diagnosis"/> is its own structured field (review item C11: "ne
/// oslanjati se samo na slobodni tekst i treatment entry") rather than text
/// folded into <see cref="Description"/>.
///
/// <see cref="ISoftDelete"/>: deleting an entry archives it instead of a
/// physical removal, so the treatment-history audit trail
/// (<see cref="MedicalRecordAuditLog"/>) always still has a real row to point
/// at - same reasoning as <c>Patient</c>/<c>MedicalDocument</c>. The global
/// query filter means a deleted entry simply stops appearing in
/// <c>MedicalRecordService.LoadFullRecordAsync</c>'s <c>Include(r =&gt;
/// r.Entries)</c>, with no extra filtering code needed at that call site.
/// </summary>
public class MedicalRecordEntry : ISoftDelete
{
    public int Id { get; set; }

    public int MedicalRecordId { get; set; }
    public MedicalRecord MedicalRecord { get; set; } = null!;

    public DateOnly EntryDate { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
