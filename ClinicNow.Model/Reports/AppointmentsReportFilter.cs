namespace ClinicNow.Model.Reports;

using ClinicNow.Model.Common;

/// <summary>
/// Filters for the Appointments PDF report (design doc §4). Dates are plain
/// clinic-local calendar dates - <c>ReportService</c> converts them to UTC
/// via <see cref="ClinicTimeZone"/>, keeping timezone math server-side and
/// out of the Flutter client entirely.
/// </summary>
public class AppointmentsReportFilter
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>Null/omitted means "all doctors."</summary>
    public int? DoctorId { get; set; }

    /// <summary>Null/empty means "all statuses."</summary>
    public List<AppointmentStatus>? Statuses { get; set; }
}
