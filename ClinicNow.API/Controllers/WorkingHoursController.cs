using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// A doctor's recurring weekly availability. Reads open to every authenticated
/// role (needed to compute free booking slots, Phase 4); writes Administrator/
/// Staff only.
/// </summary>
public class WorkingHoursController : BaseCRUDController<WorkingHoursDto, WorkingHoursSearchObject, WorkingHoursInsertRequest, WorkingHoursUpdateRequest>
{
    public WorkingHoursController(
        ICRUDService<WorkingHoursDto, WorkingHoursSearchObject, WorkingHoursInsertRequest, WorkingHoursUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<WorkingHoursDto>> Insert(WorkingHoursInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<WorkingHoursDto>> Update(int id, WorkingHoursUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
