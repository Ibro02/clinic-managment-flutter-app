namespace ClinicNow.Model.Common;

/// <summary>
/// Lifecycle states for an <c>Appointment</c> (the spec's "Termin") - see CLAUDE.md §8
/// and rulebook Part II §G: Pending -&gt; Confirmed -&gt; Completed, with Cancelled
/// reachable from any non-terminal state.
///
/// This value only ever changes through the appointment state machine
/// (<c>ClinicNow.Services.AppointmentStateMachine</c>, added in Phase 4) - never by
/// editing the column directly, and never by hard-deleting the row (hard delete
/// instead of a status change is treated as a defect per the rulebook).
/// </summary>
public enum AppointmentStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3
}
