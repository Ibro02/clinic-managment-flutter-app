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
    private readonly IReportService _service;

    public ReportsController(IReportService service)
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

    // The same two reports as data, so the desktop can chart them beside the
    // PDF (review item C8). Same filters, same aggregation - a chart that
    // disagreed with the document printed next to it would be worse than no
    // chart at all.

    [HttpGet("appointments-data")]
    public Task<AppointmentsReportData> AppointmentsData([FromQuery] AppointmentsReportFilter filter, CancellationToken cancellationToken) =>
        _service.GetAppointmentsReportDataAsync(filter, cancellationToken);

    [HttpGet("revenue-data")]
    public Task<RevenueReportData> RevenueData([FromQuery] RevenueReportFilter filter, CancellationToken cancellationToken) =>
        _service.GetRevenueReportDataAsync(filter, cancellationToken);
}
