namespace ClinicNow.Model.Dto;

public class PatientDto
{
    public int Id { get; set; }

    /// <summary>Null for a walk-in patient record with no login account.</summary>
    public int? UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PersonalIdNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
