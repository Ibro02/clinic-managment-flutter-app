using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Lab findings ("laboratorijski nalazi") tied to a specific appointment
/// (review item C4). Reads open to Administrator/Staff/Doctor for any
/// patient, and to Patient for their own record only (ownership enforced
/// server-side in the service, never trusted from the client). Entering a
/// finding is a doctor-or-lab-staff action, matching the prijava's
/// description; deleting is Administrator/Staff only, same gate as
/// <see cref="MedicalDocumentController"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LabFindingController : ControllerBase
{
    private readonly ILabFindingService _service;

    public LabFindingController(ILabFindingService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<LabFindingDto>>> GetPaged(
        [FromQuery] LabFindingSearchObject search, CancellationToken cancellationToken) =>
        Ok(await _service.GetPagedAsync(search, cancellationToken));

    [HttpPost]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    public async Task<ActionResult<LabFindingDto>> Create(LabFindingInsertRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var finding = await _service.GetFileForDownloadAsync(id, cancellationToken);
        return this.CacheableFile(finding.FileData, finding.ContentType, finding.ContentHash, finding.FileName);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
