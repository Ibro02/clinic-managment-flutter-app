using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Doctor profiles. Reads are open to every authenticated role (patients need to
/// browse doctors while booking, Phase 4); writes (which also provision the
/// doctor's login account) are Administrator/Staff only.
/// </summary>
public class DoctorController : BaseCRUDController<DoctorDto, DoctorSearchObject, DoctorInsertRequest, DoctorUpdateRequest>
{
    public DoctorController(ICRUDService<DoctorDto, DoctorSearchObject, DoctorInsertRequest, DoctorUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<DoctorDto>> Insert(DoctorInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<DoctorDto>> Update(int id, DoctorUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
