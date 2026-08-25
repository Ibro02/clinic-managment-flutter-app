namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A doctor's profile. Always linked to a real login <see cref="User"/> (Doctor
/// role) - created together as one operation by an administrator/staff member via
/// <c>DoctorService.InsertAsync</c> (rulebook §5: staff/doctor accounts are never
/// self-registered).
/// </summary>
public class Doctor
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>The one clinic/branch this doctor practices at - a doctor works at a single Location (1:1), never chosen independently when booking an appointment with them.</summary>
    public int LocationId { get; set; }

    public Location Location { get; set; } = null!;

    public string? LicenseNumber { get; set; }

    public string? Bio { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<DoctorSpecialization> DoctorSpecializations { get; set; } = [];

    public ICollection<WorkingHours> WorkingHoursList { get; set; } = [];

    public ICollection<ScheduleBlock> ScheduleBlocks { get; set; } = [];
}
