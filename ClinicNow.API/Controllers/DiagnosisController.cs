using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// The diagnosis codebook. Reads inherit the base controller's <c>[Authorize]</c>
/// and stay open to every signed-in role - a Doctor has to be able to fill the
/// dropdown when writing a medical-record entry - while writes are
/// Administrator/Staff only, like every other codebook.
/// </summary>
public class DiagnosisController
    : BaseCRUDController<DiagnosisDto, DiagnosisSearchObject, DiagnosisInsertRequest, DiagnosisUpdateRequest>
{
    public DiagnosisController(
        ICRUDService<DiagnosisDto, DiagnosisSearchObject, DiagnosisInsertRequest, DiagnosisUpdateRequest> service)
        : base(service)
    {
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<DiagnosisDto>> Insert(DiagnosisInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<DiagnosisDto>> Update(int id, DiagnosisUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
