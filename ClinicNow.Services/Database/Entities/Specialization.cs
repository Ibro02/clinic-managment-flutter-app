namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// Reference table (codebook) of medical specializations (e.g. "Dermatologija").
/// Doctors link to these via <c>DoctorSpecialization</c> (Phase 3). Not counted
/// toward the ≥10 non-reference tables (CLAUDE.md §6).
/// </summary>
public class Specialization
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
