using ClinicNow.Model.Reports;

namespace ClinicNow.Services.Reports;

/// <summary>
/// The two business reports, in both shapes they are needed in: a printable PDF
/// and the aggregate figures behind it. Renamed from <c>IReportPdfService</c>
/// when the data methods were added (review item C8) - the reports are no
/// longer PDF-only, and the interface name should say what it does.
/// </summary>
public interface IReportService
{
    Task<byte[]> GenerateAppointmentsReportAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default);

    Task<byte[]> GenerateRevenueReportAsync(RevenueReportFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Per-doctor appointment counts by status, for the same filter the PDF uses.</summary>
    Task<AppointmentsReportData> GetAppointmentsReportDataAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Per-service net revenue, for the same filter the PDF uses.</summary>
    Task<RevenueReportData> GetRevenueReportDataAsync(RevenueReportFilter filter, CancellationToken cancellationToken = default);
}
