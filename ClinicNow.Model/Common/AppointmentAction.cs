namespace ClinicNow.Model.Common;

/// <summary>
/// The transitions the appointment state machine can expose from a given status,
/// via each state class's <c>AllowedActions()</c> override. Surfaced to clients as
/// <c>AppointmentDto.AllowedActions</c> so the UI can disable buttons that would
/// fail server-side anyway, with a reason, instead of a user hitting a 400
/// (rulebook Part II §K: "disabled-with-reason for unavailable actions").
/// </summary>
public enum AppointmentAction
{
    Confirm,
    Complete,
    Cancel,
    Reschedule
}
