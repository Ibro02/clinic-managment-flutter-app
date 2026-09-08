namespace ClinicNow.Model.Dto;

/// <summary>
/// One row of the read-only "Statusi termina" codebook tab (review item S2):
/// what a status means and which transitions the state machine actually allows
/// from it right now, straight from <c>BaseAppointmentState.AllowedActions()</c> -
/// not a hand-maintained copy that could drift from the real rules.
/// </summary>
public class AppointmentStatusInfoDto
{
    public Common.AppointmentStatus Status { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> AllowedActions { get; set; } = [];
}
