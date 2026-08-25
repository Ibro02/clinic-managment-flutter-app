using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

public class MedicalServiceController
    : BaseCRUDController<MedicalServiceDto, MedicalServiceSearchObject, MedicalServiceInsertRequest, MedicalServiceUpdateRequest>
{
    public MedicalServiceController(
        ICRUDService<MedicalServiceDto, MedicalServiceSearchObject, MedicalServiceInsertRequest, MedicalServiceUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<MedicalServiceDto>> Insert(MedicalServiceInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<MedicalServiceDto>> Update(int id, MedicalServiceUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
