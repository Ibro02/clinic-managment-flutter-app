namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A doctor's recurring weekly availability window (e.g. "Monday 08:00-16:00").
/// Read by the appointment booking flow (Phase 4) to compute real free slots -
/// never trust the client for "what's available" (rulebook Part II §G).
/// </summary>
public class WorkingHours
{
    public int Id { get; set; }

    public int DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}
