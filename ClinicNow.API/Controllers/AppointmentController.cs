using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Patient}")]
    public async Task<ActionResult<AppointmentDto>> Schedule(AppointmentInsertRequest request, CancellationToken cancellationToken)
    {
        var created = await _appointmentService.ScheduleAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public async Task<ActionResult<AppointmentDto>> Confirm(int id, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.ConfirmAsync(id, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public async Task<ActionResult<AppointmentDto>> Complete(int id, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.CompleteAsync(id, cancellationToken));

    /// <summary>Open to every authenticated role - ownership (a Patient may only cancel their own) is enforced in the service, not here.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<AppointmentDto>> Cancel(int id, AppointmentCancelRequest request, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.CancelAsync(id, request, cancellationToken));

    /// <summary>Real free slots for a doctor+service on a given day - drives the mobile booking flow's time picker (rulebook §7: only real free slots offered).</summary>
    [HttpGet("available-slots")]
    public async Task<ActionResult<List<DateTime>>> GetAvailableSlots(
        [FromQuery] int doctorId, [FromQuery] int medicalServiceId, [FromQuery] DateOnly date, CancellationToken cancellationToken) =>
        Ok(await _appointmentService.GetAvailableSlotsAsync(doctorId, medicalServiceId, date, cancellationToken));
}
