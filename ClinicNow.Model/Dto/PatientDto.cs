using ClinicNow.Model.Common;

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

    public Gender? Gender { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Every patient has one - convenience id so the UI can deep-link straight to the medical record without an extra lookup.</summary>
    public int? MedicalRecordId { get; set; }
}
