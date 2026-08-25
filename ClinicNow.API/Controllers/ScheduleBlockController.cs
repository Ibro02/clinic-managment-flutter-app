using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Blocked periods on a doctor's schedule (vacation, meeting, ...). Reads open to
/// every authenticated role (needed to exclude blocked times when computing free
/// booking slots, Phase 4); writes Administrator/Staff only.
/// </summary>
public class ScheduleBlockController : BaseCRUDController<ScheduleBlockDto, ScheduleBlockSearchObject, ScheduleBlockInsertRequest, ScheduleBlockUpdateRequest>
{
    public ScheduleBlockController(
        ICRUDService<ScheduleBlockDto, ScheduleBlockSearchObject, ScheduleBlockInsertRequest, ScheduleBlockUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<ScheduleBlockDto>> Insert(ScheduleBlockInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<ScheduleBlockDto>> Update(int id, ScheduleBlockUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
