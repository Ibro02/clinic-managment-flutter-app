namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A blocked period on a doctor's schedule (vacation, meeting, ...) - carved out
/// of their otherwise-recurring <see cref="WorkingHours"/> when the booking flow
/// (Phase 4) computes real free slots. Stored in UTC (CLAUDE.md "Time Handling").
/// </summary>
public class ScheduleBlock
{
    public int Id { get; set; }

    public int DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string Reason { get; set; } = string.Empty;
}
