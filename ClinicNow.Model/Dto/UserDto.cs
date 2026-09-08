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

    /// <summary>
    /// Whether this account receives the pre-appointment reminder by email
    /// (review item C7) - the persisted state behind the mobile settings
    /// toggle, so the switch shows what the server actually holds.
    /// </summary>
    public bool EmailRemindersEnabled { get; set; }

    /// <summary>
    /// The mobile app's "jezik aplikacije" preference (review item 7) - "bs" or
    /// "en", see <see cref="Localization.PatientLanguage"/>.
    /// </summary>
    public string PreferredLanguage { get; set; } = Localization.PatientLanguage.Bosnian;

    /// <summary>Role names (e.g. "Administrator"), never role IDs (rulebook Part II §K).</summary>
    public List<string> Roles { get; set; } = [];
}
