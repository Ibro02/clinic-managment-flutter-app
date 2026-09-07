namespace ClinicNow.Model.Reports;

/// <summary>Filters for the Revenue PDF report (design doc §4).</summary>
public class RevenueReportFilter
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Null/omitted means "all services" (review item C8 - the report used to
    /// offer period only). Filters the payment <em>items</em>, so a period's
    /// revenue can be read one service at a time rather than only as a whole.
    /// </summary>
    public int? MedicalServiceId { get; set; }
}
