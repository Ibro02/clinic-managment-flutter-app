namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// Reference table (codebook) of diagnoses, keyed by their ICD-10 code.
///
/// Exists so a <see cref="MedicalRecordEntry"/> records a diagnosis as a
/// structured reference rather than free text (prijava §4.1: "Dijagnoza se ne
/// upisuje kao slobodan tekst nego kao strukturiran zapis, tako da se kasnije
/// može pretraživati i iskoristiti kao osnova za uputnicu specijalisti"; the
/// August review asked for the same thing in its item 11). Being a real table
/// also satisfies rulebook §3.1: reference data is an FK to its own table, never
/// a string column.
///
/// A codebook, so it is <em>not</em> counted toward the ≥10 non-reference tables
/// (CLAUDE.md §6) - but it does get full CRUD like every other codebook
/// (rulebook §2.2).
/// </summary>
public class Diagnosis
{
    public int Id { get; set; }

    /// <summary>ICD-10 code, e.g. "J06.9". Unique - it is what identifies the diagnosis clinically.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable name, e.g. "Akutna infekcija gornjih disajnih puteva".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which specialization normally treats this diagnosis, when there is an
    /// obvious one. Optional (a common cold belongs to no specialist), and what
    /// lets the referral dialog pre-select a target specialization from the
    /// diagnosis the doctor recorded.
    /// </summary>
    public int? SuggestedSpecializationId { get; set; }

    public Specialization? SuggestedSpecialization { get; set; }
}
