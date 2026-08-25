namespace ClinicNow.Model.Dto;

/// <summary>
/// Public shape of a <c>User</c> - never the EF entity, never includes
/// <c>PasswordHash</c> (rulebook Part II §D: EF entities are never returned).
/// </summary>
public class UserDto
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Role names (e.g. "Administrator"), never role IDs (rulebook Part II §K).</summary>
    public List<string> Roles { get; set; } = [];
}
