using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

public class SpecializationController
    : BaseCRUDController<SpecializationDto, SpecializationSearchObject, SpecializationInsertRequest, SpecializationUpdateRequest>
{
    public SpecializationController(
        ICRUDService<SpecializationDto, SpecializationSearchObject, SpecializationInsertRequest, SpecializationUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<SpecializationDto>> Insert(SpecializationInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<SpecializationDto>> Update(int id, SpecializationUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
