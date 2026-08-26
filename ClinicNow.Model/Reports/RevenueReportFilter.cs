namespace ClinicNow.Model.Reports;

/// <summary>Filters for the Revenue PDF report (design doc §4) - period only.</summary>
public class RevenueReportFilter
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
