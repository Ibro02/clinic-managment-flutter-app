using ClinicNow.Model.Reports;

namespace ClinicNow.Services.Reports;

public interface IReportPdfService
{
    Task<byte[]> GenerateAppointmentsReportAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default);

    Task<byte[]> GenerateRevenueReportAsync(RevenueReportFilter filter, CancellationToken cancellationToken = default);
}
