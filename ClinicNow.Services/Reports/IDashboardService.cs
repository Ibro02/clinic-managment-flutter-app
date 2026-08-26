using ClinicNow.Model.Dto;

namespace ClinicNow.Services.Reports;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
