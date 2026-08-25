using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// City codebook. Reading requires only a valid token (any role - patients need
/// this to browse locations while booking); writes are restricted to
/// Administrator/Staff (rulebook Part II §F: role-based on admin/management
/// endpoints).
/// </summary>
public class CityController : BaseCRUDController<CityDto, CitySearchObject, CityInsertRequest, CityUpdateRequest>
{
    public CityController(ICRUDService<CityDto, CitySearchObject, CityInsertRequest, CityUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<CityDto>> Insert(CityInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<CityDto>> Update(int id, CityUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
