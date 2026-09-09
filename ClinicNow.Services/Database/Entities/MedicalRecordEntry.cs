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

    /// <summary>
    /// The diagnosis as a structured reference into the <see cref="Entities.Diagnosis"/>
    /// codebook, not free text - prijava §4.1 ("Dijagnoza se ne upisuje kao
    /// slobodan tekst nego kao strukturiran zapis") and the August review's item
    /// 11. Being an FK also satisfies rulebook §3.1's rule that reference data is
    /// never stored as a string column.
    /// </summary>
    public int DiagnosisId { get; set; }
    public Diagnosis Diagnosis { get; set; } = null!;

    /// <summary>
    /// Optional free-text qualifier on top of the coded diagnosis (e.g. "lijeva
    /// strana, drugi recidiv"). Deliberately additive: it can never stand in for
    /// the code, only refine it.
    /// </summary>
    public string? DiagnosisNote { get; set; }

    public string Treatment { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
