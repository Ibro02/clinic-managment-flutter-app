using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Services.Appointments;

/// <summary>
/// The single definition of "which slots inside one clinic-local day are
/// actually free" (review item C19). The rule is pure: given a day's working
/// windows plus the blocks and appointments that overlap it, walk the windows at
/// service-duration granularity and keep the slots that are neither past,
/// blocked, nor taken.
///
/// It is kept free of <c>DbContext</c> so the same algorithm can be fed two
/// different ways - <see cref="AppointmentService.GetAvailableSlotsAsync"/>
/// queries one doctor/day at a time, while the recommender batch-loads its whole
/// lookahead window once and evaluates every (doctor, service, day) in memory.
/// Both shapes, one definition; the same reasoning
/// <see cref="DoctorCompatibility"/> is built on, and the reason the recommender
/// can stop issuing SQL in a triple loop without growing a second, drifting copy
/// of the availability rule.
/// </summary>
public static class SlotGeneration
{
    /// <summary>A busy span - a schedule block or an existing appointment.</summary>
    public readonly record struct Interval(DateTime StartUtc, DateTime EndUtc);

    /// <summary>
    /// Free slots on <paramref name="date"/>, ascending per window.
    /// <paramref name="windows"/> must already be the doctor's working hours for
    /// that day of week; <paramref name="blocks"/> and <paramref name="busy"/>
    /// need only *include* the spans overlapping the day - anything wider is
    /// harmless, since each is tested by overlap against the individual slot.
    /// </summary>
    public static List<DateTime> FreeSlots(
        DateOnly date,
        IReadOnlyCollection<WorkingHours> windows,
        IReadOnlyCollection<Interval> blocks,
        IReadOnlyCollection<Interval> busy,
        TimeSpan duration,
        DateTime nowUtc)
    {
        var slots = new List<DateTime>();

        foreach (var window in windows)
        {
            // WorkingHours holds clinic wall-clock times, so both window edges
            // resolve through ClinicTimeZone rather than being stamped UTC
            // (review item C1).
            var slotStart = ClinicTimeZone.ToUtc(date, window.StartTime);
            var windowEnd = ClinicTimeZone.ToUtc(date, window.EndTime);

            while (slotStart + duration <= windowEnd)
            {
                var slotEnd = slotStart + duration;

                var isPast = slotStart <= nowUtc;
                var isBlocked = blocks.Any(b => b.StartUtc < slotEnd && b.EndUtc > slotStart);
                var isTaken = busy.Any(e => e.StartUtc < slotEnd && e.EndUtc > slotStart);

                if (!isPast && !isBlocked && !isTaken)
                {
                    slots.Add(slotStart);
                }

                slotStart = slotEnd; // back-to-back slots at service-duration granularity, no gaps
            }
        }

        return slots;
    }
}
