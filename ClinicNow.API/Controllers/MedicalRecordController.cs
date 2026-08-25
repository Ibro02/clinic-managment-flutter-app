using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// The medical file ("medicinski karton"). Reads: Administrator/Staff/Doctor
/// for any patient, Patient for their own only (ownership enforced in the
/// service). Writes: a Doctor can only append notes and add treatment-history
/// rows (never edit/delete existing content); an Administrator has full CRUD
/// over the same content, per the explicit requirement.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MedicalRecordController : ControllerBase
{
    private readonly IMedicalRecordService _service;

    public MedicalRecordController(IMedicalRecordService service)
    {
        _service = service;
    }

    [HttpGet("patient/{patientId:int}")]
    public async Task<ActionResult<MedicalRecordDto>> GetByPatient(int patientId, CancellationToken cancellationToken) =>
        Ok(await _service.GetByPatientIdAsync(patientId, cancellationToken));

    [HttpPost("patient/{patientId:int}/notes/append")]
    [Authorize(Roles = $"{Roles.Doctor},{Roles.Administrator}")]
    public async Task<ActionResult<MedicalRecordDto>> AppendNotes(int patientId, MedicalRecordAppendNotesRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.AppendNotesAsync(patientId, request, cancellationToken));

    [HttpPut("patient/{patientId:int}/notes")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<ActionResult<MedicalRecordDto>> ReplaceNotes(int patientId, MedicalRecordUpdateNotesRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.ReplaceNotesAsync(patientId, request, cancellationToken));

    [HttpPost("patient/{patientId:int}/entries")]
    [Authorize(Roles = $"{Roles.Doctor},{Roles.Administrator}")]
    public async Task<ActionResult<MedicalRecordDto>> AddEntry(int patientId, MedicalRecordEntryInsertRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.AddEntryAsync(patientId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("entries/{entryId:int}")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<ActionResult<MedicalRecordDto>> UpdateEntry(int entryId, MedicalRecordEntryUpdateRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.UpdateEntryAsync(entryId, request, cancellationToken));

    [HttpDelete("entries/{entryId:int}")]
    [Authorize(Roles = Roles.Administrator)]
    public async Task<IActionResult> DeleteEntry(int entryId, CancellationToken cancellationToken)
    {
        await _service.DeleteEntryAsync(entryId, cancellationToken);
        return NoContent();
    }
}
