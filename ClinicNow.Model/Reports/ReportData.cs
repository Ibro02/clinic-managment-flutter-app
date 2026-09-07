namespace ClinicNow.Model.Reports;

/// <summary>
/// The same figures the PDF reports print, as data (review item C8: both
/// reports need a graphical view, and a client can't chart a byte array).
///
/// Deliberately the aggregate rows only - never the underlying appointments or
/// payments. A chart needs one number per bar; shipping every row to draw a
/// dozen of them would be the "list endpoint returns heavy payloads" defect the
/// rulebook calls out (Part II §D). The PDF and these DTOs are produced by the
/// same aggregation in <c>ReportService</c>, so a chart can never disagree with
/// the document printed beside it.
/// </summary>
public class RevenueReportRow
{
    public string ServiceName { get; set; } = string.Empty;

    public int PaymentCount { get; set; }

    /// <summary>Captured minus refunded, so a refunded visit doesn't read as revenue.</summary>
    public decimal NetTotalEur { get; set; }
}

public class RevenueReportData
{
    public List<RevenueReportRow> Rows { get; set; } = [];

    public decimal GrandTotalEur { get; set; }
}

/// <summary>One doctor's appointment counts, split by status, for the period.</summary>
public class AppointmentsReportRow
{
    public string DoctorName { get; set; } = string.Empty;

    public int PendingCount { get; set; }

    public int ConfirmedCount { get; set; }

    public int CompletedCount { get; set; }

    public int CancelledCount { get; set; }

    public int TotalCount { get; set; }
}

public class AppointmentsReportData
{
    public List<AppointmentsReportRow> Rows { get; set; } = [];

    public int TotalCount { get; set; }
}
