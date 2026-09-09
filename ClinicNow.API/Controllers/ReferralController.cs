using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Specialist referrals ("uputnice") - review item C5. Reads open to
/// Administrator/Staff/Doctor for any patient, and to Patient for their own
/// record only (ownership enforced server-side in the service, never trusted
/// from the client). Creating one is a clinical action during an
/// examination - Administrator/Doctor only, same gate
/// <c>MedicalRecordController.AddEntry</c> uses. There is deliberately no
/// Update endpoint. Delete is Administrator-only - a referral is otherwise
/// permanent for Doctor/Staff, but an Administrator can remove a mistaken
/// entry (soft-delete, never a physical removal).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReferralController : ControllerBase
{
    private readonly IReferralService _service;

    public ReferralController(IReferralService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ReferralDto>>> GetPaged(
        [FromQuery] ReferralSearchObject criteria, CancellationToken cancellationToken) =>
        Ok(await _service.GetPagedAsync(criteria, cancellationToken));

    [HttpPost]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Doctor}")]
    public async Task<ActionResult<ReferralDto>> Create(ReferralInsertRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
