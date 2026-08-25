namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// Pure M:N join between <see cref="Doctor"/> and <see cref="Specialization"/> - no
/// extra attributes of its own, so per CLAUDE.md §6 this is a reference/join table
/// and does not count toward the ≥10 non-reference tables.
/// </summary>
public class DoctorSpecialization
{
    public int DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    public int SpecializationId { get; set; }

    public Specialization Specialization { get; set; } = null!;
}
