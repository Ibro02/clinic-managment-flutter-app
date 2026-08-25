namespace ClinicNow.Model.Requests;

/// <summary>Creates a walk-in patient record (staff-entered, no login account). Registration via the mobile app creates its own linked record instead - see <c>RegisterRequest</c>.</summary>
public class PatientInsertRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PersonalIdNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }
}

public class PatientUpdateRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PersonalIdNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }
}
