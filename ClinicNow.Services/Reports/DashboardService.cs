using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Services.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ClinicNow.Services.Reports;

public class DashboardService : IDashboardService
{
    private const string CacheKey = "dashboard:summary";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly ClinicNowContext _context;
    private readonly IMemoryCache _cache;

    public DashboardService(ClinicNowContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out DashboardSummaryDto? cached) && cached is not null)
        {
            return cached;
        }

        var summary = await ComputeSummaryAsync(cancellationToken);
        _cache.Set(CacheKey, summary, CacheDuration);
        return summary;
    }

    private async Task<DashboardSummaryDto> ComputeSummaryAsync(CancellationToken cancellationToken)
    {
        var todayLocal = DateOnly.FromDateTime(ClinicTimeZone.NowLocal);
        var (newCount, existingCount) = await GetNewVsExistingPatientsAsync(todayLocal, cancellationToken);

        return new DashboardSummaryDto
        {
            TodayAppointmentsCount = await CountAppointmentsOnAsync(todayLocal, cancellationToken),
            ActivePatientsCount = await _context.Patients.CountAsync(cancellationToken),
            AvailableDoctorsCount = await CountAvailableDoctorsNowAsync(cancellationToken),
            MonthlyRevenueEur = await GetMonthlyRevenueEurAsync(cancellationToken),
            WeeklyTrend = await GetWeeklyTrendAsync(todayLocal, cancellationToken),
            NewPatientsCount30d = newCount,
            ExistingPatientsCount30d = existingCount
        };
    }

    private async Task<int> CountAppointmentsOnAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var startUtc = ClinicTimeZone.LocalDateStartUtc(date);
        var endUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(date);
        return await _context.Appointments.CountAsync(a => a.StartUtc >= startUtc && a.StartUtc < endUtc, cancellationToken);
    }

    /// <summary>
    /// "On duty right now" (design doc §2/§5): active doctors whose recurring
    /// WorkingHours cover this exact moment today, minus any doctor currently
    /// inside a ScheduleBlock - two bounded queries, never a per-doctor loop.
    /// </summary>
    private async Task<int> CountAvailableDoctorsNowAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        // WorkingHours.StartTime/EndTime are clinic wall-clock values, so "on duty
        // right now" is a clinic-local question - resolved through ClinicTimeZone,
        // the same way the booking engine reads these columns (review item C1).
        var todayDayOfWeek = ClinicTimeZone.LocalDayOfWeekOf(nowUtc);
        var timeNow = ClinicTimeZone.LocalTimeOf(nowUtc);

        var onDutyDoctorIds = await _context.Doctors
            .Where(d => d.User.IsActive)
            .Where(d => d.WorkingHoursList.Any(wh =>
                wh.DayOfWeek == todayDayOfWeek && wh.StartTime <= timeNow && timeNow < wh.EndTime))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        if (onDutyDoctorIds.Count == 0)
        {
            return 0;
        }

        var blockedDoctorIds = await _context.ScheduleBlocks
            .Where(b => onDutyDoctorIds.Contains(b.DoctorId) && b.StartUtc <= nowUtc && nowUtc <= b.EndUtc)
            .Select(b => b.DoctorId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return onDutyDoctorIds.Except(blockedDoctorIds).Count();
    }

    private async Task<decimal> GetMonthlyRevenueEurAsync(CancellationToken cancellationToken)
    {
        var monthStartUtc = ClinicTimeZone.StartOfThisMonthUtc();
        var nowUtc = DateTime.UtcNow;

        var payments = await _context.Payments
            .Include(p => p.Refunds)
            .Where(p => p.Status != PaymentStatus.Pending && p.PaidAtUtc != null
                && p.PaidAtUtc >= monthStartUtc && p.PaidAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);

        return payments.Sum(p => p.AmountEur - p.Refunds.Sum(r => r.AmountEur));
    }

    /// <summary>7 sequential day-bucket counts (today back 6) - simple and correct across the local-midnight boundary; a single GroupBy can't express the per-day timezone shift as cleanly.</summary>
    private async Task<List<WeeklyTrendPointDto>> GetWeeklyTrendAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var trend = new List<WeeklyTrendPointDto>();
        for (var offset = 6; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            trend.Add(new WeeklyTrendPointDto
            {
                Date = date,
                AppointmentCount = await CountAppointmentsOnAsync(date, cancellationToken)
            });
        }
        return trend;
    }

    /// <summary>
    /// Among patients with at least one appointment (any status - booking
    /// activity, not just completed visits) in the last 30 days, a patient is
    /// "new" if that window also contains their chronologically-first-ever
    /// appointment; otherwise "existing" (design doc §5).
    /// </summary>
    private async Task<(int NewCount, int ExistingCount)> GetNewVsExistingPatientsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var windowStartUtc = ClinicTimeZone.LocalDateStartUtc(today.AddDays(-29));
        var windowEndUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(today);

        var activePatientIds = await _context.Appointments
            .Where(a => a.StartUtc >= windowStartUtc && a.StartUtc < windowEndUtc)
            .Select(a => a.PatientId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (activePatientIds.Count == 0)
        {
            return (0, 0);
        }

        var firstAppointmentByPatient = await _context.Appointments
            .Where(a => activePatientIds.Contains(a.PatientId))
            .GroupBy(a => a.PatientId)
            .Select(g => g.Min(a => a.StartUtc))
            .ToListAsync(cancellationToken);

        var newCount = firstAppointmentByPatient.Count(firstStartUtc => firstStartUtc >= windowStartUtc);
        return (newCount, firstAppointmentByPatient.Count - newCount);
    }
}
