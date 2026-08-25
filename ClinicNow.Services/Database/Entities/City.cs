namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// Reference table (codebook) of cities. <see cref="Location"/> FKs to this rather
/// than storing a city name as a free-text string (rulebook Part II §A: reference
/// data is always a FK to its own table). Not counted toward the ≥10 non-reference
/// tables (CLAUDE.md §6).
/// </summary>
public class City
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Location> Locations { get; set; } = [];
}
