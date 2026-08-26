using ClinicNow.Model.Dto;
using ClinicNow.Model.Security;
using ClinicNow.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>Clinic-wide KPI summary (Phase 9, design doc §5) - Administrator/Staff only, same gate as `ReportsController`.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    public DashboardController(IDashboardService service)
    {
        _service = service;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken) =>
        Ok(await _service.GetSummaryAsync(cancellationToken));
}
