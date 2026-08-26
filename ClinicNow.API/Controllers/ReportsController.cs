using ClinicNow.Model.Reports;
using ClinicNow.Model.Security;
using ClinicNow.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>Server-side PDF reports (Phase 9, design doc §4) - Administrator/Staff only.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
public class ReportsController : ControllerBase
{
    private readonly IReportPdfService _service;

    public ReportsController(IReportPdfService service)
    {
        _service = service;
    }

    [HttpGet("appointments-pdf")]
    public async Task<IActionResult> AppointmentsPdf([FromQuery] AppointmentsReportFilter filter, CancellationToken cancellationToken)
    {
        var bytes = await _service.GenerateAppointmentsReportAsync(filter, cancellationToken);
        return File(bytes, "application/pdf", $"izvjestaj-termini-{filter.StartDate:yyyyMMdd}-{filter.EndDate:yyyyMMdd}.pdf");
    }

    [HttpGet("revenue-pdf")]
    public async Task<IActionResult> RevenuePdf([FromQuery] RevenueReportFilter filter, CancellationToken cancellationToken)
    {
        var bytes = await _service.GenerateRevenueReportAsync(filter, cancellationToken);
        return File(bytes, "application/pdf", $"izvjestaj-prihodi-{filter.StartDate:yyyyMMdd}-{filter.EndDate:yyyyMMdd}.pdf");
    }
}
