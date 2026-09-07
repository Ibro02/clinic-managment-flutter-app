using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.People;
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
    private readonly IDoctorService _service;

    public DoctorController(IDoctorService service) : base(service)
    {
        _service = service;
    }

    /// <summary>
    /// The signed-in doctor's own profile, resolved from the token - never a
    /// route id (rulebook §5). Read-only by design: clinic, specializations and
    /// licence are Administrator/Staff-owned, so a doctor can see them on their
    /// profile screen without any route to editing them.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Roles = Roles.Doctor)]
    public async Task<ActionResult<DoctorDto>> Me(CancellationToken cancellationToken) =>
        Ok(await _service.GetOwnAsync(cancellationToken));

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
