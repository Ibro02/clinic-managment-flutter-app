namespace ClinicNow.Model.Dto;

/// <summary>One point in the dashboard's 7-day appointment-volume trend.</summary>
public class WeeklyTrendPointDto
{
    public DateOnly Date { get; set; }

    public int AppointmentCount { get; set; }
}

/// <summary>
/// The Phase 9 dashboard aggregate (design doc §5) - every figure computed
/// in clinic-local time (<see cref="ClinicNow.Model.Common.ClinicTimeZone"/>),
/// cached briefly server-side (<c>DashboardService</c>).
/// </summary>
public class DashboardSummaryDto
{
    public int TodayAppointmentsCount { get; set; }

    public int ActivePatientsCount { get; set; }

    public int AvailableDoctorsCount { get; set; }

    public decimal MonthlyRevenueEur { get; set; }

    public List<WeeklyTrendPointDto> WeeklyTrend { get; set; } = [];

    public int NewPatientsCount30d { get; set; }

    public int ExistingPatientsCount30d { get; set; }
}
