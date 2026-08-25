namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A patient's medical record. Legally-retained (soft-delete only, never a hard
/// DELETE - CLAUDE.md §5/§6). <see cref="UserId"/> is nullable and optional: a
/// patient who self-registers via the mobile app gets one automatically (see
/// <c>UserService.RegisterAsync</c>), but staff can also create a walk-in record
/// for a patient who has no login account at all.
/// </summary>
public class Patient : ISoftDelete
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>National ID number (e.g. JMBG). Nullable until known - not always captured at self-registration time.</summary>
    public string? PersonalIdNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
}
