using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicNow.API.Controllers;

/// <summary>
/// The appointment lifecycle. Deliberately not a <c>BaseCRUDController</c> - there
/// is no generic Update or Delete here, only the specific state transitions
/// (rulebook §8: status changes only ever go through the state machine, and an
/// appointment is never hard-deleted). Every action still contains zero business
/// logic itself; all of it - including who's allowed to do what - lives in
/// <see cref="IAppointmentService"/> (rulebook Part II §D).
/// </summary>
public class AppointmentController : BaseController<AppointmentDto, AppointmentSearchObject>
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentController(IAppointmentService appointmentService) : base(appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Patient}")]
    public async Task<ActionResult<AppointmentDto>> Schedule(AppointmentInsertRequest request, CancellationToken cancellationToken)
    {
        var created = await _appointmentService.ScheduleAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("{id:int}/confirm")]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public async Task<ActionResult<AppointmentDto>> Confirm(int id, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.ConfirmAsync(id, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public async Task<ActionResult<AppointmentDto>> Complete(int id, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.CompleteAsync(id, cancellationToken));

    /// <summary>Open to every authenticated role - ownership (a Patient may only cancel their own) is enforced in the service, not here.</summary>
    [HttpPost("{id:int}/cancel")]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    public async Task<ActionResult<AppointmentDto>> Cancel(int id, AppointmentCancelRequest request, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.CancelAsync(id, request, cancellationToken));

    /// <summary>Moves an appointment to a new doctor/time (review item C6) - open to every authenticated role, ownership and the 48h patient cutoff are enforced in the service, same shape as <see cref="Cancel"/>.</summary>
    [HttpPost("{id:int}/reschedule")]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    public async Task<ActionResult<AppointmentDto>> Reschedule(int id, AppointmentRescheduleRequest request, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.RescheduleAsync(id, request, cancellationToken));

    /// <summary>Real free slots for a doctor+service on a given day - drives the mobile booking flow's time picker (rulebook §7: only real free slots offered).</summary>
    [HttpGet("available-slots")]
    public async Task<ActionResult<List<DateTime>>> GetAvailableSlots(
        [FromQuery] int doctorId, [FromQuery] int medicalServiceId, [FromQuery] DateOnly date, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.GetAvailableSlotsAsync(doctorId, medicalServiceId, date, cancellationToken));

    /// <summary>The read-only "Statusi termina" codebook tab (review item S2) - live state-machine data, not a hardcoded list.</summary>
    [HttpGet("statuses")]
    public ActionResult<IReadOnlyList<AppointmentStatusInfoDto>> GetStatusInfo() =>
        Ok(_appointmentService.GetStatusInfo());
}
