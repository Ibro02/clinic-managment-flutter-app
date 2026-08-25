namespace ClinicNow.Model.Requests;

/// <summary>
/// Creates a Doctor profile together with its login account in one operation -
/// a doctor never self-registers (rulebook §5). The server assigns the Doctor
/// role; it is never taken from the client.
/// </summary>
public class DoctorInsertRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>The one clinic this doctor will practice at - required, a doctor never works at more than one Location.</summary>
    public int LocationId { get; set; }

    public string? LicenseNumber { get; set; }

    public string? Bio { get; set; }

    public List<int> SpecializationIds { get; set; } = [];
}

/// <summary>Updates profile fields on both the Doctor record and its linked User. Never changes email/password - see rulebook Part II §E for the separate password-change flow.</summary>
public class DoctorUpdateRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public int LocationId { get; set; }

    public string? LicenseNumber { get; set; }

    public string? Bio { get; set; }

    public List<int> SpecializationIds { get; set; } = [];
}
