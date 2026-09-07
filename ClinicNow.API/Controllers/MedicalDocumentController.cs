using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Findings & files on a patient record (CLAUDE.md §6). Reads open to
/// Administrator/Staff/Doctor for any patient, and to Patient for their own
/// record only (ownership enforced server-side in the service, never
/// trusted from the client). Uploading/deleting is clinic-staff work only -
/// patients never attach their own documents in this workflow.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MedicalDocumentController : ControllerBase
{
    private readonly IMedicalDocumentService _service;

    public MedicalDocumentController(IMedicalDocumentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<MedicalDocumentDto>>> GetPaged(
        [FromQuery] MedicalDocumentSearchObject search, CancellationToken cancellationToken) =>
        Ok(await _service.GetPagedAsync(search, cancellationToken));

    [HttpPost]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff},{Roles.Doctor}")]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    public async Task<ActionResult<MedicalDocumentDto>> Upload(MedicalDocumentInsertRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.UploadAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var document = await _service.GetFileForDownloadAsync(id, cancellationToken);
        return this.CacheableFile(document.FileData, document.ContentType, document.ContentHash, document.FileName);
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting(RateLimiterPolicies.Write)]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
