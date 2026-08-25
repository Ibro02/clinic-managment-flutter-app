namespace ClinicNow.Model.Dto;

/// <summary>
/// Denormalizes the linked User's profile fields (rulebook Part II §K: never
/// show raw IDs) and the doctor's specializations both as display names
/// (<see cref="Specializations"/>) and as editable IDs (<see cref="SpecializationIds"/>).
/// </summary>
public class DoctorDto
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>The one clinic this doctor practices at (rulebook Part II §K: never a raw ID - see also <see cref="LocationName"/>).</summary>
    public int LocationId { get; set; }

    public string LocationName { get; set; } = string.Empty;

    public string? LicenseNumber { get; set; }

    public string? Bio { get; set; }

    public List<string> Specializations { get; set; } = [];

    public List<int> SpecializationIds { get; set; } = [];
}
