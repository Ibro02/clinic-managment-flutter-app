namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A clinic branch/site (reference table/codebook - CLAUDE.md §6). Appointments
/// (Phase 4) take place at a <see cref="Location"/>. FKs to <see cref="City"/>
/// rather than storing a city name inline (rulebook Part II §A).
/// </summary>
public class Location
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public int CityId { get; set; }

    public City City { get; set; } = null!;
}
