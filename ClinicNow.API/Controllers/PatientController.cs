using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.People;
using MapsterMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Patient medical records. Only clinic staff/doctors browse the full list -
/// a patient never sees other patients' data (rulebook §5/§7: ownership is
/// enforced server-side, never trusted from the client). A patient's own
/// record is available separately via <see cref="Me"/>.
/// </summary>
public class PatientController : BaseCRUDController<PatientDto, PatientSearchObject, PatientInsertRequest, PatientUpdateRequest>
{
    private readonly IPatientService _service;

    public PatientController(IPatientService service) : base(service)
    {
        _service = service;
    }

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public override Task<ActionResult<Model.Common.PagedResult<PatientDto>>> GetPaged(
        [FromQuery] PatientSearchObject criteria, CancellationToken cancellationToken) =>
        base.GetPaged(criteria, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public override Task<ActionResult<PatientDto>> GetById(int id, CancellationToken cancellationToken) =>
        base.GetById(id, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<PatientDto>> Insert(PatientInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<ActionResult<PatientDto>> Update(int id, PatientUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);

    /// <summary>The current Patient user's own medical record, looked up by the
    /// validated token's user ID - never a route/body parameter (rulebook §5).</summary>
    [HttpGet("me")]
    [Authorize(Roles = Roles.Patient)]
    public async Task<ActionResult<PatientDto>> Me(CancellationToken cancellationToken) =>
        Ok(await _service.GetOwnAsync(cancellationToken));

    /// <summary>Un-archives a soft-deleted patient (and reactivates their linked login) - Administrator/Staff only, same gate as <see cref="Delete"/>.</summary>
    [HttpPost("{id:int}/restore")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public async Task<ActionResult<PatientDto>> Restore(int id, CancellationToken cancellationToken) =>
        Ok(await _service.RestoreAsync(id, cancellationToken));
}
