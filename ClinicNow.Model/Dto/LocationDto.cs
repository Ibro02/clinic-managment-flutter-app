namespace ClinicNow.Model.Dto;

/// <summary>
/// Carries both <c>CityId</c> (the real FK, used by insert/update) and
/// <c>CityName</c> (denormalized purely for display) - never raw IDs in the UI,
/// per rulebook Part II §K.
/// </summary>
public class LocationDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public int CityId { get; set; }

    public string CityName { get; set; } = string.Empty;
}
