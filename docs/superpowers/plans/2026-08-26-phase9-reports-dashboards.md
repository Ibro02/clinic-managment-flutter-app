# Phase 9 Reports & Dashboards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Administrator/Staff two downloadable/printable PDF business reports (Appointments, Revenue) and a live KPI dashboard, reading only existing data — no new database tables.

**Architecture:** Two new bespoke backend services under `ClinicNow.Services/Reports/` — `DashboardService` (EF aggregate queries, `IMemoryCache`-backed) and `ReportPdfService` (QuestPDF document generation) — behind two new controllers (`DashboardController`, `ReportsController`). A shared `ClinicTimeZone` helper centralizes all Europe/Sarajevo conversions. On the desktop client, a new `DashboardScreen` (default landing for Administrator/Staff, replacing the existing "Početna" placeholder) uses `fl_chart`; a new `ReportsScreen` (new "Izvještaji" nav entry) renders generated PDFs inline via the `printing` package's `PdfPreview` widget, which also supplies the print button.

**Tech Stack:** ASP.NET Core / EF Core / SQL Server (existing, no schema change), QuestPDF (new, PDF generation, Community license), Flutter `fl_chart` (new, charts) + `printing` (new, PDF preview/print).

**Spec:** [docs/superpowers/specs/2026-08-26-phase9-reports-dashboards-design.md](../specs/2026-08-26-phase9-reports-dashboards-design.md). Also: `CLAUDE.md` Part II, `PLAN.md` Phase 9.

## Global Constraints

- No new database table/migration — this phase only reads `Appointment`, `Payment`, `PaymentItem`, `PaymentRefund`, `Patient`, `Doctor`, `WorkingHours`/`WorkingHoursEntries`, `ScheduleBlock`, `MedicalService`.
- Revenue = **collected** basis: `Payment` rows with `Status != Pending` and a non-null `PaidAtUtc`, net of `Refunds.Sum(AmountEur)`, grouped by `PaymentItem.MedicalService` — never `MedicalService.Price × appointment count`.
- Reports & Dashboard are **Administrator + Staff only** (`[Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]`), matching `PaymentController`'s refund-endpoint pattern.
- All "today"/"this week"/"this month" computations go through one shared `ClinicTimeZone` helper (Europe/Sarajevo) — no ad-hoc `DateTime.Now`/`TimeZoneInfo` calls anywhere else.
- Clients send plain `DateOnly` calendar dates for report filters; the server converts to UTC ranges — no timezone math in Flutter.
- Date ranges are capped at 366 days and validated `start <= end`, via `ValidationException` (→ existing `ExceptionFilter`, no new exception types needed).
- Services touching `DbContext` are registered `Scoped`; `ILogger<T>` on any failure; no `Console.WriteLine`/`.Result`/`.Wait()`; async EF calls throughout.
- No backend automated test project in this codebase (established convention) — every task is verified live against a real running instance.

---

## File Structure

**Backend — new files:**
- `ClinicNow.Model/Common/ClinicTimeZone.cs` — Europe/Sarajevo conversion helper.
- `ClinicNow.Model/Reports/AppointmentsReportFilter.cs`, `RevenueReportFilter.cs` — `[FromQuery]`-bound filter classes.
- `ClinicNow.Model/Dto/DashboardSummaryDto.cs` — includes `WeeklyTrendPointDto`.
- `ClinicNow.Services/Reports/IDashboardService.cs`, `DashboardService.cs`.
- `ClinicNow.Services/Reports/IReportPdfService.cs`, `ReportPdfService.cs`.
- `ClinicNow.API/Controllers/DashboardController.cs`.
- `ClinicNow.API/Controllers/ReportsController.cs`.

**Backend — modified files:**
- `ClinicNow.Services/ClinicNow.Services.csproj` — add QuestPDF package.
- `ClinicNow.API/Program.cs` — QuestPDF license, DI for the two new services.

**Desktop (`clinicnow_desktop`) — new files:**
- `lib/models/dashboard_summary.dart`.
- `lib/core/reports_api.dart` — plain client (like `AuthApi`), not a `BaseProvider<T>` subclass.
- `lib/screens/dashboard/dashboard_screen.dart`.
- `lib/screens/reports/reports_screen.dart`, `appointments_report_tab.dart`, `revenue_report_tab.dart`.

**Desktop — modified files:**
- `pubspec.yaml` — add `fl_chart`, `printing`.
- `lib/layouts/app_shell.dart` — gate "Početna" to Administrator/Staff → `DashboardScreen`; add "Izvještaji" nav entry (Administrator/Staff) → `ReportsScreen`.

---

## Task 1: Model-layer types (`ClinicTimeZone`, report filters, `DashboardSummaryDto`)

**Files:**
- Create: `ClinicNow.Model/Common/ClinicTimeZone.cs`
- Create: `ClinicNow.Model/Reports/AppointmentsReportFilter.cs`
- Create: `ClinicNow.Model/Reports/RevenueReportFilter.cs`
- Create: `ClinicNow.Model/Dto/DashboardSummaryDto.cs`

**Interfaces:**
- Consumes: `ClinicNow.Model.Common.AppointmentStatus` (existing).
- Produces: `ClinicTimeZone.{NowLocal, ToLocal(DateTime), LocalDateStartUtc(DateOnly), LocalDateEndExclusiveUtc(DateOnly), StartOfThisMonthUtc()}`, `AppointmentsReportFilter{StartDate,EndDate,DoctorId,Statuses}`, `RevenueReportFilter{StartDate,EndDate}`, `DashboardSummaryDto`, `WeeklyTrendPointDto{Date,AppointmentCount}` — consumed by Tasks 2-4.

- [ ] **Step 1: `ClinicTimeZone.cs`**

```csharp
namespace ClinicNow.Model.Common;

/// <summary>
/// Centralizes the fixed clinic-local timezone (Europe/Sarajevo) so
/// "today"/"this week"/"this month" are computed consistently regardless of
/// what timezone the API container or a client machine happens to run in
/// (design doc §2/§3). .NET 6+ resolves IANA ids like this one on Windows as
/// well as Linux, so this works unchanged in Docker and on a Windows dev
/// machine. Every dashboard/report date computation goes through this one
/// class - no ad-hoc DateTime.Now/TimeZoneInfo calls elsewhere.
/// </summary>
public static class ClinicTimeZone
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sarajevo");

    /// <summary>The current instant, expressed in clinic-local time.</summary>
    public static DateTime NowLocal => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Converts a UTC instant to clinic-local time.</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>The UTC instant corresponding to local midnight on <paramref name="date"/>.</summary>
    public static DateTime LocalDateStartUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Zone);

    /// <summary>
    /// The UTC instant for the start of the local calendar day AFTER
    /// <paramref name="date"/> - an exclusive upper bound, so a range query
    /// is always `start &lt;= x &amp;&amp; x &lt; LocalDateEndExclusiveUtc(date)`,
    /// never an off-by-one around midnight.
    /// </summary>
    public static DateTime LocalDateEndExclusiveUtc(DateOnly date) => LocalDateStartUtc(date.AddDays(1));

    /// <summary>The UTC instant for the start of the current local calendar month.</summary>
    public static DateTime StartOfThisMonthUtc()
    {
        var today = DateOnly.FromDateTime(NowLocal);
        return LocalDateStartUtc(new DateOnly(today.Year, today.Month, 1));
    }
}
```

- [ ] **Step 2: `AppointmentsReportFilter.cs`**

```csharp
namespace ClinicNow.Model.Reports;

using ClinicNow.Model.Common;

/// <summary>
/// Filters for the Appointments PDF report (design doc §4). Dates are plain
/// clinic-local calendar dates - <c>ReportPdfService</c> converts them to UTC
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
```

- [ ] **Step 3: `RevenueReportFilter.cs`**

```csharp
namespace ClinicNow.Model.Reports;

/// <summary>Filters for the Revenue PDF report (design doc §4) - period only.</summary>
public class RevenueReportFilter
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
```

- [ ] **Step 4: `DashboardSummaryDto.cs`**

```csharp
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
```

- [ ] **Step 5: Build & verify**

```bash
cd ClinicNow && dotnet build ClinicNow.Model/ClinicNow.Model.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add ClinicNow.Model/Common/ClinicTimeZone.cs ClinicNow.Model/Reports/AppointmentsReportFilter.cs ClinicNow.Model/Reports/RevenueReportFilter.cs ClinicNow.Model/Dto/DashboardSummaryDto.cs
git commit -m "feat(reports): add model-layer types (ClinicTimeZone, report filters, DashboardSummaryDto)"
```

---

## Task 2: `IDashboardService`/`DashboardService`

**Files:**
- Create: `ClinicNow.Services/Reports/IDashboardService.cs`
- Create: `ClinicNow.Services/Reports/DashboardService.cs`

**Interfaces:**
- Consumes: `DashboardSummaryDto`/`WeeklyTrendPointDto` (Task 1), `ClinicTimeZone` (Task 1), `ClinicNowContext` (existing), `IMemoryCache` (already globally registered in `Program.cs`), `PaymentStatus` (existing, `ClinicNow.Model.Common`).
- Produces: `IDashboardService.GetSummaryAsync(CancellationToken) : Task<DashboardSummaryDto>` — consumed by Task 4 (`DashboardController`).

- [ ] **Step 1: `IDashboardService.cs`**

```csharp
using ClinicNow.Model.Dto;

namespace ClinicNow.Services.Reports;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: `DashboardService.cs`**

Every "available doctors" / "new vs existing" query below is bounded (at most a handful of queries per call, never per-row), and the whole result is cached for 60s so a dashboard reload/auto-refresh doesn't re-run all of them on every request (rulebook Part II §D: cache per-request data with `IMemoryCache`).

```csharp
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
    /// WorkingHours cover this exact clinic-local moment today, minus any
    /// doctor currently inside a ScheduleBlock - two bounded queries, never a
    /// per-doctor loop.
    /// </summary>
    private async Task<int> CountAvailableDoctorsNowAsync(CancellationToken cancellationToken)
    {
        var nowLocal = ClinicTimeZone.NowLocal;
        var todayDayOfWeek = nowLocal.DayOfWeek;
        var timeNow = TimeOnly.FromDateTime(nowLocal);

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

        var nowUtc = DateTime.UtcNow;
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
```

- [ ] **Step 3: Build & verify**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 warnings/errors (compiles standalone even though nothing registers `IDashboardService` in DI yet — that's Task 4).

- [ ] **Step 4: Commit**

```bash
git add ClinicNow.Services/Reports/IDashboardService.cs ClinicNow.Services/Reports/DashboardService.cs
git commit -m "feat(reports): implement DashboardService (KPI aggregates, IMemoryCache-backed)"
```

---

## Task 3: `IReportPdfService`/`ReportPdfService` (QuestPDF)

**Files:**
- Modify: `ClinicNow.Services/ClinicNow.Services.csproj` — add QuestPDF package reference.
- Create: `ClinicNow.Services/Reports/IReportPdfService.cs`
- Create: `ClinicNow.Services/Reports/ReportPdfService.cs`

**Interfaces:**
- Consumes: `AppointmentsReportFilter`/`RevenueReportFilter` (Task 1), `ClinicTimeZone` (Task 1), `ClinicNowContext`, `AppointmentStatusExtensions.ToDisplayName()` (existing), `ValidationException` (existing).
- Produces: `IReportPdfService.{GenerateAppointmentsReportAsync, GenerateRevenueReportAsync} : Task<byte[]>` — consumed by Task 4 (`ReportsController`).

- [ ] **Step 1: Add the QuestPDF package**

Edit `ClinicNow.Services/ClinicNow.Services.csproj`, adding this line inside the existing `<ItemGroup>` of `<PackageReference>`s (alphabetically, after `Microsoft.ML`):

```xml
    <PackageReference Include="QuestPDF" Version="2026.7.2" />
```

- [ ] **Step 2: `IReportPdfService.cs`**

```csharp
using ClinicNow.Model.Reports;

namespace ClinicNow.Services.Reports;

public interface IReportPdfService
{
    Task<byte[]> GenerateAppointmentsReportAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default);

    Task<byte[]> GenerateRevenueReportAsync(RevenueReportFilter filter, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: `ReportPdfService.cs`**

Note that `QuestPDF.Settings.License` is set once at API startup (Task 4's `Program.cs` change) — calling `GeneratePdf()` before that is set throws at runtime, so this task's own build-and-verify is compile-only; the actual PDF bytes are exercised live in Task 4.

```csharp
using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Reports;
using ClinicNow.Services.Database;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClinicNow.Services.Reports;

public class ReportPdfService : IReportPdfService
{
    private readonly ClinicNowContext _context;

    public ReportPdfService(ClinicNowContext context)
    {
        _context = context;
    }

    public async Task<byte[]> GenerateAppointmentsReportAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.StartDate, filter.EndDate);

        var startUtc = ClinicTimeZone.LocalDateStartUtc(filter.StartDate);
        var endUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(filter.EndDate);

        var query = _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Patient)
            .Include(a => a.MedicalService)
            .Where(a => a.StartUtc >= startUtc && a.StartUtc < endUtc);

        if (filter.DoctorId.HasValue)
        {
            query = query.Where(a => a.DoctorId == filter.DoctorId.Value);
        }

        if (filter.Statuses is { Count: > 0 })
        {
            query = query.Where(a => filter.Statuses.Contains(a.Status));
        }

        // One query with joins via Include, materialized once - the per-doctor
        // grouping below runs in memory against that single result set, never
        // a per-doctor round trip (rulebook Part II §D: no N+1).
        var appointments = await query.OrderBy(a => a.StartUtc).ToListAsync(cancellationToken);

        var doctorGroups = appointments
            .GroupBy(a => new { a.DoctorId, DoctorName = $"{a.Doctor.User.FirstName} {a.Doctor.User.LastName}" })
            .OrderBy(g => g.Key.DoctorName)
            .ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(column =>
                {
                    column.Item().Text("Izvještaj o terminima").FontSize(18).Bold();
                    column.Item().Text($"Period: {filter.StartDate:dd.MM.yyyy} - {filter.EndDate:dd.MM.yyyy}").FontSize(10);
                });

                page.Content().PaddingTop(10).Column(column =>
                {
                    if (doctorGroups.Count == 0)
                    {
                        column.Item().Text("Nema termina za odabrani period i filtere.");
                    }

                    foreach (var group in doctorGroups)
                    {
                        column.Item().PaddingTop(12).Text($"{group.Key.DoctorName} — {group.Count()} termina").FontSize(12).Bold();

                        column.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCellStyle).Text("Datum i vrijeme");
                                header.Cell().Element(HeaderCellStyle).Text("Pacijent");
                                header.Cell().Element(HeaderCellStyle).Text("Usluga");
                                header.Cell().Element(HeaderCellStyle).Text("Status");
                            });

                            foreach (var appointment in group.OrderBy(a => a.StartUtc))
                            {
                                var localStart = ClinicTimeZone.ToLocal(appointment.StartUtc);
                                table.Cell().Element(RowCellStyle).Text(localStart.ToString("dd.MM.yyyy HH:mm"));
                                table.Cell().Element(RowCellStyle).Text($"{appointment.Patient.FirstName} {appointment.Patient.LastName}");
                                table.Cell().Element(RowCellStyle).Text(appointment.MedicalService.Name);
                                table.Cell().Element(RowCellStyle).Text(appointment.Status.ToDisplayName());
                            }
                        });
                    }

                    column.Item().PaddingTop(16).LineHorizontal(1);
                    column.Item().PaddingTop(4).Text($"Ukupno termina: {appointments.Count}").FontSize(11).Bold();
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Stranica ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    public async Task<byte[]> GenerateRevenueReportAsync(RevenueReportFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.StartDate, filter.EndDate);

        var startUtc = ClinicTimeZone.LocalDateStartUtc(filter.StartDate);
        var endUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(filter.EndDate);

        var payments = await _context.Payments
            .Include(p => p.Items).ThenInclude(i => i.MedicalService)
            .Include(p => p.Refunds)
            .Where(p => p.Status != PaymentStatus.Pending && p.PaidAtUtc != null
                && p.PaidAtUtc >= startUtc && p.PaidAtUtc < endUtc)
            .ToListAsync(cancellationToken);

        // Single-item-per-payment assumption carried over from Phase 8 (design
        // doc §4 / Payments design doc §9) - the net amount is attributed
        // whole to that one item's service, never split proportionally.
        var rows = payments
            .SelectMany(p => p.Items.Select(item => new
            {
                ServiceName = item.MedicalService.Name,
                NetAmountEur = p.AmountEur - p.Refunds.Sum(r => r.AmountEur)
            }))
            .GroupBy(x => x.ServiceName)
            .Select(g => new { ServiceName = g.Key, PaymentCount = g.Count(), NetTotalEur = g.Sum(x => x.NetAmountEur) })
            .OrderByDescending(x => x.NetTotalEur)
            .ToList();

        var grandTotal = rows.Sum(r => r.NetTotalEur);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(column =>
                {
                    column.Item().Text("Izvještaj o prihodima").FontSize(18).Bold();
                    column.Item().Text($"Period: {filter.StartDate:dd.MM.yyyy} - {filter.EndDate:dd.MM.yyyy}").FontSize(10);
                });

                page.Content().PaddingTop(10).Column(column =>
                {
                    if (rows.Count == 0)
                    {
                        column.Item().Text("Nema naplaćenih uplata za odabrani period.");
                        return;
                    }

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCellStyle).Text("Usluga");
                            header.Cell().Element(HeaderCellStyle).AlignRight().Text("Broj uplata");
                            header.Cell().Element(HeaderCellStyle).AlignRight().Text("Iznos (EUR)");
                        });

                        foreach (var row in rows)
                        {
                            table.Cell().Element(RowCellStyle).Text(row.ServiceName);
                            table.Cell().Element(RowCellStyle).AlignRight().Text(row.PaymentCount.ToString());
                            table.Cell().Element(RowCellStyle).AlignRight().Text(row.NetTotalEur.ToString("F2"));
                        }
                    });

                    column.Item().PaddingTop(16).LineHorizontal(1);
                    column.Item().PaddingTop(4).AlignRight().Text($"Ukupan prihod: {grandTotal:F2} EUR").FontSize(11).Bold();
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Stranica ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void ValidateRange(DateOnly start, DateOnly end)
    {
        if (start == default || end == default)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["startDate"] = ["Odaberite period izvještaja."] });
        }
        if (start > end)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["endDate"] = ["Krajnji datum mora biti nakon početnog datuma."] });
        }
        if (end.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue) > TimeSpan.FromDays(366))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["endDate"] = ["Period izvještaja ne može biti duži od godinu dana."] });
        }
    }

    private static IContainer HeaderCellStyle(IContainer container) =>
        container.DefaultTextStyle(x => x.Bold()).PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer RowCellStyle(IContainer container) =>
        container.PaddingVertical(3).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
}
```

- [ ] **Step 4: Restore & build**

```bash
cd ClinicNow && dotnet restore ClinicNow.Services/ClinicNow.Services.csproj && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: QuestPDF restores and the project builds with 0 errors. If the pinned `2026.7.2` version doesn't resolve, bump to whatever the latest QuestPDF version on nuget.org actually is at execution time and re-run — this is a normal dependency-pin adjustment (same class of fix as this codebase's existing `Microsoft.EntityFrameworkCore.Tools` preview pin), not a design change.

- [ ] **Step 5: Commit**

```bash
git add ClinicNow.Services/ClinicNow.Services.csproj ClinicNow.Services/Reports/IReportPdfService.cs ClinicNow.Services/Reports/ReportPdfService.cs
git commit -m "feat(reports): implement ReportPdfService (QuestPDF appointments + revenue PDFs)"
```

---

## Task 4: Controllers, DI wiring, and backend live verification

**Files:**
- Create: `ClinicNow.API/Controllers/DashboardController.cs`
- Create: `ClinicNow.API/Controllers/ReportsController.cs`
- Modify: `ClinicNow.API/Program.cs`

**Interfaces:**
- Consumes: `IDashboardService` (Task 2), `IReportPdfService` (Task 3), `AppointmentsReportFilter`/`RevenueReportFilter` (Task 1), `Roles` (existing).
- Produces: `GET api/Dashboard/summary`, `GET api/Reports/appointments-pdf`, `GET api/Reports/revenue-pdf` — consumed by Task 5 (`ReportsApi`).

- [ ] **Step 1: `DashboardController.cs`**

```csharp
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
```

- [ ] **Step 2: `ReportsController.cs`**

```csharp
using ClinicNow.Model.Reports;
using ClinicNow.Model.Security;
using ClinicNow.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>Server-side PDF reports (Phase 9, design doc §4) - Administrator/Staff only.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
public class ReportsController : ControllerBase
{
    private readonly IReportPdfService _service;

    public ReportsController(IReportPdfService service)
    {
        _service = service;
    }

    [HttpGet("appointments-pdf")]
    public async Task<IActionResult> AppointmentsPdf([FromQuery] AppointmentsReportFilter filter, CancellationToken cancellationToken)
    {
        var bytes = await _service.GenerateAppointmentsReportAsync(filter, cancellationToken);
        return File(bytes, "application/pdf", $"izvjestaj-termini-{filter.StartDate:yyyyMMdd}-{filter.EndDate:yyyyMMdd}.pdf");
    }

    [HttpGet("revenue-pdf")]
    public async Task<IActionResult> RevenuePdf([FromQuery] RevenueReportFilter filter, CancellationToken cancellationToken)
    {
        var bytes = await _service.GenerateRevenueReportAsync(filter, cancellationToken);
        return File(bytes, "application/pdf", $"izvjestaj-prihodi-{filter.StartDate:yyyyMMdd}-{filter.EndDate:yyyyMMdd}.pdf");
    }
}
```

- [ ] **Step 3: Wire DI + QuestPDF license in `Program.cs`**

Add `using ClinicNow.Services.Reports;` and `using QuestPDF.Infrastructure;` to the top of the `using` block (next to the other `ClinicNow.Services.*` usings, e.g. right after `using ClinicNow.Services.Recommender;`).

Add this block right after the existing `// --- Payments (Phase 8) -----------------------------------------------------------` block (i.e. right after `builder.Services.AddScoped<IPaymentService, PaymentService>();`) and before the `// SignalR for real-time notification auto-refresh` comment:

```csharp
// --- Reports & Dashboard (Phase 9) ------------------------------------------------
// QuestPDF requires its license accepted exactly once per process before the
// first GeneratePdf() call - Community is the free tier and fits this project.
QuestPDF.Settings.License = LicenseType.Community;
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportPdfService, ReportPdfService>();
```

- [ ] **Step 4: Build**

```bash
cd ClinicNow && dotnet build ClinicNow.API/ClinicNow.API.csproj
```
Expected: 0 warnings/errors across the whole solution.

- [ ] **Step 5: Live-verify against a real running instance**

```bash
cd ClinicNow && dotnet run --project ClinicNow.API --urls "http://localhost:5235"
```

In another shell, log in as the seeded `staff@clinicnow.test`/`test` account and exercise all three endpoints:

```bash
TOKEN=$(curl -s -X POST http://localhost:5235/api/auth/login -H "Content-Type: application/json" -d '{"email":"staff@clinicnow.test","password":"test"}' | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")

curl -s http://localhost:5235/api/Dashboard/summary -H "Authorization: Bearer $TOKEN"

curl -s "http://localhost:5235/api/Reports/appointments-pdf?startDate=2026-01-01&endDate=2026-12-31" -H "Authorization: Bearer $TOKEN" -o /tmp/appointments-report.pdf
curl -s "http://localhost:5235/api/Reports/revenue-pdf?startDate=2026-01-01&endDate=2026-12-31" -H "Authorization: Bearer $TOKEN" -o /tmp/revenue-report.pdf
```

Confirm: the summary JSON has sane, non-negative values and a 7-element `weeklyTrend`; both PDFs are real, non-trivial files (check with `file /tmp/appointments-report.pdf` — expect `PDF document, version 1.x` and a size well above a few hundred bytes, not an empty/error stub).

Then confirm role gating with a Patient token (expect `403` on all three) and with no `Authorization` header at all (expect `401` on all three):

```bash
PATIENT_TOKEN=$(curl -s -X POST http://localhost:5235/api/auth/login -H "Content-Type: application/json" -d '{"email":"patient@clinicnow.test","password":"test"}' | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5235/api/Dashboard/summary -H "Authorization: Bearer $PATIENT_TOKEN"
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5235/api/Dashboard/summary
```

Also spot-check the Europe/Sarajevo boundary: pick (or seed via the running API) one appointment whose `startUtc` is within an hour of UTC midnight, and confirm it's attributed to the correct **local** calendar day in `weeklyTrend`/`todayAppointmentsCount` (Sarajevo is UTC+1 or UTC+2 depending on DST, so a UTC-22:30–23:59 appointment lands on the *next* local day).

- [ ] **Step 6: Commit**

```bash
git add ClinicNow.API/Controllers/DashboardController.cs ClinicNow.API/Controllers/ReportsController.cs ClinicNow.API/Program.cs
git commit -m "feat(reports): add DashboardController/ReportsController, wire DI + QuestPDF license"
```

---

## Task 5: Desktop — dependencies, models, `ReportsApi`

**Files:**
- Modify: `UI/clinicnow_desktop/pubspec.yaml`
- Create: `UI/clinicnow_desktop/lib/models/dashboard_summary.dart`
- Create: `UI/clinicnow_desktop/lib/core/reports_api.dart`

**Interfaces:**
- Consumes: `BaseProvider.baseUrl` (existing), `AuthSession` (existing), `ApiException` (existing).
- Produces: `DashboardSummary`, `WeeklyTrendPoint`, `ReportsApi.{getDashboardSummary, getAppointmentsReportPdf, getRevenueReportPdf}` — consumed by Tasks 6-7.

- [ ] **Step 1: Add dependencies to `pubspec.yaml`**

Add these two lines to the `dependencies:` block (after `file_picker: ^12.1.0`):

```yaml
  fl_chart: ^0.69.2
  printing: ^5.14.2
```

- [ ] **Step 2: `lib/models/dashboard_summary.dart`**

```dart
/// Mirrors the backend's `WeeklyTrendPointDto`.
class WeeklyTrendPoint {
  final DateTime date;
  final int appointmentCount;

  WeeklyTrendPoint({required this.date, required this.appointmentCount});

  factory WeeklyTrendPoint.fromJson(Map<String, dynamic> json) => WeeklyTrendPoint(
        date: DateTime.parse(json['date'] as String),
        appointmentCount: json['appointmentCount'] as int,
      );
}

/// Mirrors the backend's `DashboardSummaryDto`.
class DashboardSummary {
  final int todayAppointmentsCount;
  final int activePatientsCount;
  final int availableDoctorsCount;
  final double monthlyRevenueEur;
  final List<WeeklyTrendPoint> weeklyTrend;
  final int newPatientsCount30d;
  final int existingPatientsCount30d;

  DashboardSummary({
    required this.todayAppointmentsCount,
    required this.activePatientsCount,
    required this.availableDoctorsCount,
    required this.monthlyRevenueEur,
    required this.weeklyTrend,
    required this.newPatientsCount30d,
    required this.existingPatientsCount30d,
  });

  factory DashboardSummary.fromJson(Map<String, dynamic> json) => DashboardSummary(
        todayAppointmentsCount: json['todayAppointmentsCount'] as int,
        activePatientsCount: json['activePatientsCount'] as int,
        availableDoctorsCount: json['availableDoctorsCount'] as int,
        monthlyRevenueEur: (json['monthlyRevenueEur'] as num).toDouble(),
        weeklyTrend: (json['weeklyTrend'] as List<dynamic>)
            .map((e) => WeeklyTrendPoint.fromJson(e as Map<String, dynamic>))
            .toList(),
        newPatientsCount30d: json['newPatientsCount30d'] as int,
        existingPatientsCount30d: json['existingPatientsCount30d'] as int,
      );
}
```

- [ ] **Step 3: `lib/core/reports_api.dart`**

```dart
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/dashboard_summary.dart';
import 'api_exception.dart';
import 'auth_session.dart';
import 'base_provider.dart';

/// Thin client for `api/Dashboard` and `api/Reports` (Phase 9). Kept separate
/// from `BaseProvider<T>` (like `AuthApi`) since two of these calls return raw
/// PDF bytes and one returns a single aggregate object, never a paged list.
class ReportsApi {
  final AuthSession _authSession;

  ReportsApi(this._authSession);

  Uri _uri(String path, [Map<String, dynamic>? query]) {
    final normalizedBase =
        BaseProvider.baseUrl.endsWith('/') ? BaseProvider.baseUrl : '${BaseProvider.baseUrl}/';
    final uri = Uri.parse('$normalizedBase$path');
    if (query == null || query.isEmpty) return uri;

    final queryParameters = <String, dynamic>{};
    query.forEach((key, value) {
      if (value == null) return;
      queryParameters[key] = value is List ? value.map((e) => '$e').toList() : '$value';
    });
    return uri.replace(queryParameters: queryParameters);
  }

  Map<String, String> get _headers =>
      {if (_authSession.token != null) 'Authorization': 'Bearer ${_authSession.token}'};

  Future<DashboardSummary> getDashboardSummary() async {
    final response = await http.get(_uri('api/Dashboard/summary'), headers: _headers);
    _handleAuth(response);
    if (response.statusCode != 200) {
      throw ApiException(statusCode: response.statusCode, message: 'Greška prilikom učitavanja dashboarda.', fieldErrors: const {});
    }
    return DashboardSummary.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<List<int>> getAppointmentsReportPdf({
    required DateTime startDate,
    required DateTime endDate,
    int? doctorId,
    List<int>? statuses,
  }) async {
    final response = await http.get(
      _uri('api/Reports/appointments-pdf', {
        'startDate': _formatDate(startDate),
        'endDate': _formatDate(endDate),
        if (doctorId != null) 'doctorId': doctorId,
        if (statuses != null && statuses.isNotEmpty) 'statuses': statuses,
      }),
      headers: _headers,
    );
    return _pdfBytesOrThrow(response);
  }

  Future<List<int>> getRevenueReportPdf({required DateTime startDate, required DateTime endDate}) async {
    final response = await http.get(
      _uri('api/Reports/revenue-pdf', {'startDate': _formatDate(startDate), 'endDate': _formatDate(endDate)}),
      headers: _headers,
    );
    return _pdfBytesOrThrow(response);
  }

  List<int> _pdfBytesOrThrow(http.Response response) {
    _handleAuth(response);
    if (response.statusCode != 200) {
      throw ApiException(statusCode: response.statusCode, message: 'Greška prilikom generisanja izvještaja.', fieldErrors: const {});
    }
    return response.bodyBytes;
  }

  void _handleAuth(http.Response response) {
    if (response.statusCode == 401) {
      _authSession.clear();
    }
  }

  String _formatDate(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
}
```

- [ ] **Step 4: Fetch dependencies & analyze**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter pub get && flutter analyze
```
Expected: dependencies resolve and `flutter analyze` reports no issues. If `fl_chart`/`printing` fail to resolve at the pinned versions, bump to the latest resolvable version — a normal dependency-pin adjustment, same as Task 3's QuestPDF note.

- [ ] **Step 5: Commit**

```bash
git add pubspec.yaml pubspec.lock lib/models/dashboard_summary.dart lib/core/reports_api.dart
git commit -m "feat(reports): add fl_chart/printing deps, DashboardSummary model, ReportsApi"
```

---

## Task 6: `DashboardScreen` + `app_shell.dart` wiring (default landing for Administrator/Staff)

**Files:**
- Create: `UI/clinicnow_desktop/lib/screens/dashboard/dashboard_screen.dart`
- Modify: `UI/clinicnow_desktop/lib/layouts/app_shell.dart`

**Interfaces:**
- Consumes: `ReportsApi.getDashboardSummary` (Task 5), `DashboardSummary`/`WeeklyTrendPoint` (Task 5), `AuthSession`/`Roles` (existing).
- Produces: `DashboardScreen` widget — consumed by `app_shell.dart`'s nav rail.

- [ ] **Step 1: `dashboard_screen.dart`**

```dart
import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/reports_api.dart';
import '../../models/dashboard_summary.dart';

/// Landing screen for Administrator/Staff (Phase 9) - at-a-glance clinic KPIs
/// + two charts, replacing the "Početna" placeholder that has been in
/// `app_shell.dart` since Phase 0.
class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  static const _weekdayAbbrev = ['Pon', 'Uto', 'Sri', 'Čet', 'Pet', 'Sub', 'Ned'];

  late final ReportsApi _api;
  final _eurFormat = NumberFormat.currency(locale: 'en_US', symbol: 'EUR ', decimalDigits: 2);

  DashboardSummary? _summary;
  String? _error;
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
    _api = ReportsApi(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final summary = await _api.getDashboardSummary();
      if (mounted) setState(() => _summary = summary);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            const SizedBox(height: 12),
            FilledButton(onPressed: _load, child: const Text('Pokušaj ponovo')),
          ],
        ),
      );
    }

    final summary = _summary;
    if (summary == null) {
      return const Center(child: CircularProgressIndicator());
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Align(
            alignment: Alignment.centerRight,
            child: IconButton(
              tooltip: 'Osvježi',
              icon: _isLoading
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.refresh),
              onPressed: _isLoading ? null : _load,
            ),
          ),
          Wrap(
            spacing: 16,
            runSpacing: 16,
            children: [
              _KpiCard(label: 'Termini danas', value: '${summary.todayAppointmentsCount}', icon: Icons.event_outlined),
              _KpiCard(label: 'Aktivni pacijenti', value: '${summary.activePatientsCount}', icon: Icons.people_outline),
              _KpiCard(label: 'Dostupni doktori sada', value: '${summary.availableDoctorsCount}', icon: Icons.medical_services_outlined),
              _KpiCard(label: 'Prihod ovog mjeseca', value: _eurFormat.format(summary.monthlyRevenueEur), icon: Icons.payments_outlined),
            ],
          ),
          const SizedBox(height: 24),
          Text('Termini u posljednjih 7 dana', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          SizedBox(height: 220, child: _WeeklyTrendChart(trend: summary.weeklyTrend, weekdayLabel: _weekdayLabel)),
          const SizedBox(height: 24),
          Text('Novi vs. postojeći pacijenti (30 dana)', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          SizedBox(
            height: 220,
            child: _NewVsExistingChart(newCount: summary.newPatientsCount30d, existingCount: summary.existingPatientsCount30d),
          ),
        ],
      ),
    );
  }

  static String _weekdayLabel(DateTime date) => _weekdayAbbrev[date.weekday - 1];
}

class _KpiCard extends StatelessWidget {
  final String label;
  final String value;
  final IconData icon;

  const _KpiCard({required this.label, required this.value, required this.icon});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 220,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Icon(icon, size: 32, color: Theme.of(context).colorScheme.primary),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(value, style: Theme.of(context).textTheme.headlineSmall),
                    Text(label, style: Theme.of(context).textTheme.bodySmall),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _WeeklyTrendChart extends StatelessWidget {
  final List<WeeklyTrendPoint> trend;
  final String Function(DateTime) weekdayLabel;

  const _WeeklyTrendChart({required this.trend, required this.weekdayLabel});

  @override
  Widget build(BuildContext context) {
    if (trend.isEmpty) return const Center(child: Text('Nema podataka.'));
    final maxCount = trend.map((t) => t.appointmentCount).fold<int>(0, (a, b) => a > b ? a : b);

    return BarChart(
      BarChartData(
        alignment: BarChartAlignment.spaceAround,
        maxY: (maxCount + 1).toDouble(),
        titlesData: FlTitlesData(
          leftTitles: const AxisTitles(sideTitles: SideTitles(showTitles: true, reservedSize: 28)),
          topTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          rightTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          bottomTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              getTitlesWidget: (value, meta) {
                final index = value.toInt();
                if (index < 0 || index >= trend.length) return const SizedBox.shrink();
                return Padding(padding: const EdgeInsets.only(top: 4), child: Text(weekdayLabel(trend[index].date)));
              },
            ),
          ),
        ),
        borderData: FlBorderData(show: false),
        gridData: const FlGridData(show: true, drawVerticalLine: false),
        barGroups: [
          for (var i = 0; i < trend.length; i++)
            BarChartGroupData(x: i, barRods: [
              BarChartRodData(toY: trend[i].appointmentCount.toDouble(), width: 18, color: Theme.of(context).colorScheme.primary),
            ]),
        ],
      ),
    );
  }
}

class _NewVsExistingChart extends StatelessWidget {
  final int newCount;
  final int existingCount;

  const _NewVsExistingChart({required this.newCount, required this.existingCount});

  @override
  Widget build(BuildContext context) {
    if (newCount == 0 && existingCount == 0) {
      return const Center(child: Text('Nema podataka.'));
    }
    return PieChart(
      PieChartData(
        sectionsSpace: 2,
        centerSpaceRadius: 36,
        sections: [
          PieChartSectionData(value: newCount.toDouble(), title: 'Novi\n$newCount', color: Colors.teal, radius: 60),
          PieChartSectionData(value: existingCount.toDouble(), title: 'Postojeći\n$existingCount', color: Colors.blueGrey, radius: 60),
        ],
      ),
    );
  }
}
```

- [ ] **Step 2: Wire into `app_shell.dart`**

Add an import near the other screen imports (after `import '../screens/codebooks/codebooks_screen.dart';`):

```dart
import '../screens/dashboard/dashboard_screen.dart';
```

Replace the existing "Početna" `_NavEntry` (currently a placeholder `Center(child: Text(...))`) and gate it behind Administrator/Staff, same condition already used for `canManageCodebooks`. Change:

```dart
    final canManageCodebooks = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    final entries = <_NavEntry>[
      _NavEntry(
        destination: const NavigationRailDestination(
          icon: Icon(Icons.dashboard_outlined),
          selectedIcon: Icon(Icons.dashboard),
          label: Text('Početna'),
        ),
        builder: (_) => const Center(
          child: Text('ClinicNow — dashboard i moduli dolaze u narednim fazama.'),
        ),
      ),
```

to:

```dart
    final canManageCodebooks = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);
    // Same condition as canManageCodebooks today, named separately because the
    // two visibility rules (dashboard/reports vs. codebook CRUD) are
    // independent business decisions that happen to currently coincide.
    final canViewReports = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    final entries = <_NavEntry>[
      if (canViewReports)
        _NavEntry(
          destination: const NavigationRailDestination(
            icon: Icon(Icons.dashboard_outlined),
            selectedIcon: Icon(Icons.dashboard),
            label: Text('Početna'),
          ),
          builder: (_) => const DashboardScreen(),
        ),
```

(Leave every other `_NavEntry` in the list untouched for this task — "Izvještaji" is added in Task 7.)

- [ ] **Step 3: Analyze**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter analyze
```
Expected: no issues. If `fl_chart`'s resolved version renamed/removed any API used above (e.g. `tooltipBgColor`-style properties aren't used here, but `SideTitles`/`AxisTitles`/`BarChartRodData.toY` might differ slightly by version), fix the call sites to match the actual resolved API — this class of dependency-version drift is expected and precedented in this codebase (Phase 6 hit the same thing with a `file_picker` major-version API change and fixed it the same way, caught by `flutter analyze` before runtime).

- [ ] **Step 4: Commit**

```bash
git add lib/screens/dashboard/dashboard_screen.dart lib/layouts/app_shell.dart
git commit -m "feat(reports): add DashboardScreen, make it the Administrator/Staff landing screen"
```

---

## Task 7: `ReportsScreen` (filters + inline PDF preview/print) + nav wiring

**Files:**
- Create: `UI/clinicnow_desktop/lib/screens/reports/reports_screen.dart`
- Create: `UI/clinicnow_desktop/lib/screens/reports/appointments_report_tab.dart`
- Create: `UI/clinicnow_desktop/lib/screens/reports/revenue_report_tab.dart`
- Modify: `UI/clinicnow_desktop/lib/layouts/app_shell.dart`

**Interfaces:**
- Consumes: `ReportsApi.{getAppointmentsReportPdf,getRevenueReportPdf}` (Task 5), `DoctorProvider`/`Doctor` (existing).
- Produces: `ReportsScreen` widget — consumed by `app_shell.dart`'s nav rail.

- [ ] **Step 1: `appointments_report_tab.dart`**

```dart
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/reports_api.dart';
import '../../models/doctor.dart';
import '../../providers/doctor_provider.dart';

class AppointmentsReportTab extends StatefulWidget {
  const AppointmentsReportTab({super.key});

  @override
  State<AppointmentsReportTab> createState() => _AppointmentsReportTabState();
}

class _AppointmentsReportTabState extends State<AppointmentsReportTab> {
  static const _statusOptions = <int, String>{0: 'Na čekanju', 1: 'Potvrđen', 2: 'Završen', 3: 'Otkazan'};

  late final ReportsApi _api;
  late final DoctorProvider _doctorProvider;

  List<Doctor> _doctors = [];
  int? _selectedDoctorId;
  final Set<int> _selectedStatuses = {};
  DateTime? _startDate;
  DateTime? _endDate;

  Uint8List? _pdfBytes;
  String? _error;
  bool _isGenerating = false;

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _api = ReportsApi(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _loadDoctors();
  }

  Future<void> _loadDoctors() async {
    try {
      final result = await _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'});
      if (mounted) setState(() => _doctors = result.resultList);
    } on ApiException {
      // "Svi doktori" still works even if the dropdown list itself failed to load.
    }
  }

  Future<void> _pickDate({required bool isStart}) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: (isStart ? _startDate : _endDate) ?? now,
      firstDate: DateTime(now.year - 5),
      lastDate: DateTime(now.year + 1),
    );
    if (picked == null) return;
    setState(() {
      if (isStart) {
        _startDate = picked;
      } else {
        _endDate = picked;
      }
    });
  }

  Future<void> _generate() async {
    final start = _startDate;
    final end = _endDate;
    if (start == null || end == null) {
      setState(() => _error = 'Odaberite period izvještaja.');
      return;
    }

    setState(() {
      _isGenerating = true;
      _error = null;
    });
    try {
      final bytes = await _api.getAppointmentsReportPdf(
        startDate: start,
        endDate: end,
        doctorId: _selectedDoctorId,
        statuses: _selectedStatuses.isEmpty ? null : _selectedStatuses.toList(),
      );
      if (mounted) setState(() => _pdfBytes = Uint8List.fromList(bytes));
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isGenerating = false);
    }
  }

  String _dateLabel(DateTime? d) =>
      d == null ? 'Odaberite datum' : '${d.day.toString().padLeft(2, '0')}.${d.month.toString().padLeft(2, '0')}.${d.year}';

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 16,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              OutlinedButton.icon(
                icon: const Icon(Icons.calendar_today_outlined),
                label: Text('Od: ${_dateLabel(_startDate)}'),
                onPressed: () => _pickDate(isStart: true),
              ),
              OutlinedButton.icon(
                icon: const Icon(Icons.calendar_today_outlined),
                label: Text('Do: ${_dateLabel(_endDate)}'),
                onPressed: () => _pickDate(isStart: false),
              ),
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int?>(
                  value: _selectedDoctorId,
                  decoration: const InputDecoration(labelText: 'Doktor'),
                  items: [
                    const DropdownMenuItem<int?>(value: null, child: Text('Svi doktori')),
                    for (final doctor in _doctors) DropdownMenuItem<int?>(value: doctor.id, child: Text(doctor.fullName)),
                  ],
                  onChanged: (value) => setState(() => _selectedDoctorId = value),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            children: [
              for (final entry in _statusOptions.entries)
                FilterChip(
                  label: Text(entry.value),
                  selected: _selectedStatuses.contains(entry.key),
                  onSelected: (selected) => setState(() {
                    if (selected) {
                      _selectedStatuses.add(entry.key);
                    } else {
                      _selectedStatuses.remove(entry.key);
                    }
                  }),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Text('Bez odabranog statusa = svi statusi.', style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton.icon(
                icon: _isGenerating
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.picture_as_pdf_outlined),
                label: const Text('Generiraj'),
                onPressed: _isGenerating ? null : _generate,
              ),
              if (_error != null) ...[
                const SizedBox(width: 12),
                Expanded(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
              ],
            ],
          ),
          const SizedBox(height: 12),
          if (_pdfBytes != null)
            Expanded(
              child: PdfPreview(
                build: (format) async => _pdfBytes!,
                canChangeOrientation: false,
                canChangePageFormat: false,
                canDebug: false,
                pdfFileName: 'izvjestaj-termini.pdf',
              ),
            ),
        ],
      ),
    );
  }
}
```

- [ ] **Step 2: `revenue_report_tab.dart`**

```dart
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/reports_api.dart';

class RevenueReportTab extends StatefulWidget {
  const RevenueReportTab({super.key});

  @override
  State<RevenueReportTab> createState() => _RevenueReportTabState();
}

class _RevenueReportTabState extends State<RevenueReportTab> {
  late final ReportsApi _api;

  DateTime? _startDate;
  DateTime? _endDate;
  Uint8List? _pdfBytes;
  String? _error;
  bool _isGenerating = false;

  @override
  void initState() {
    super.initState();
    _api = ReportsApi(context.read<AuthSession>());
  }

  Future<void> _pickDate({required bool isStart}) async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: (isStart ? _startDate : _endDate) ?? now,
      firstDate: DateTime(now.year - 5),
      lastDate: DateTime(now.year + 1),
    );
    if (picked == null) return;
    setState(() {
      if (isStart) {
        _startDate = picked;
      } else {
        _endDate = picked;
      }
    });
  }

  Future<void> _generate() async {
    final start = _startDate;
    final end = _endDate;
    if (start == null || end == null) {
      setState(() => _error = 'Odaberite period izvještaja.');
      return;
    }

    setState(() {
      _isGenerating = true;
      _error = null;
    });
    try {
      final bytes = await _api.getRevenueReportPdf(startDate: start, endDate: end);
      if (mounted) setState(() => _pdfBytes = Uint8List.fromList(bytes));
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isGenerating = false);
    }
  }

  String _dateLabel(DateTime? d) =>
      d == null ? 'Odaberite datum' : '${d.day.toString().padLeft(2, '0')}.${d.month.toString().padLeft(2, '0')}.${d.year}';

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 16,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              OutlinedButton.icon(
                icon: const Icon(Icons.calendar_today_outlined),
                label: Text('Od: ${_dateLabel(_startDate)}'),
                onPressed: () => _pickDate(isStart: true),
              ),
              OutlinedButton.icon(
                icon: const Icon(Icons.calendar_today_outlined),
                label: Text('Do: ${_dateLabel(_endDate)}'),
                onPressed: () => _pickDate(isStart: false),
              ),
              FilledButton.icon(
                icon: _isGenerating
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.picture_as_pdf_outlined),
                label: const Text('Generiraj'),
                onPressed: _isGenerating ? null : _generate,
              ),
            ],
          ),
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ],
          const SizedBox(height: 12),
          if (_pdfBytes != null)
            Expanded(
              child: PdfPreview(
                build: (format) async => _pdfBytes!,
                canChangeOrientation: false,
                canChangePageFormat: false,
                canDebug: false,
                pdfFileName: 'izvjestaj-prihodi.pdf',
              ),
            ),
        ],
      ),
    );
  }
}
```

- [ ] **Step 3: `reports_screen.dart`**

```dart
import 'package:flutter/material.dart';

import 'appointments_report_tab.dart';
import 'revenue_report_tab.dart';

/// Hosts the two Phase 9 PDF reports as tabs. Each tab is its own focused
/// widget owning its own filter form + generated-PDF state.
class ReportsScreen extends StatefulWidget {
  const ReportsScreen({super.key});

  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> with SingleTickerProviderStateMixin {
  late final TabController _tabController;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 2, vsync: this);
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        TabBar(
          controller: _tabController,
          tabs: const [Tab(text: 'Izvještaj o terminima'), Tab(text: 'Izvještaj o prihodima')],
        ),
        Expanded(
          child: TabBarView(
            controller: _tabController,
            children: const [AppointmentsReportTab(), RevenueReportTab()],
          ),
        ),
      ],
    );
  }
}
```

- [ ] **Step 4: Wire into `app_shell.dart`**

Add an import next to the Task 6 import:

```dart
import '../screens/reports/reports_screen.dart';
```

Insert a new nav entry right after "Termini" and before the `if (canManageCodebooks)` entry:

```dart
      _NavEntry(
        destination: const NavigationRailDestination(
          icon: Icon(Icons.event_outlined),
          selectedIcon: Icon(Icons.event),
          label: Text('Termini'),
        ),
        builder: (_) => const AppointmentScreen(),
      ),
      if (canViewReports)
        _NavEntry(
          destination: const NavigationRailDestination(
            icon: Icon(Icons.bar_chart_outlined),
            selectedIcon: Icon(Icons.bar_chart),
            label: Text('Izvještaji'),
          ),
          builder: (_) => const ReportsScreen(),
        ),
      if (canManageCodebooks)
```

- [ ] **Step 5: Analyze**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter analyze
```
Expected: no issues. As in Task 6, fix any `printing`/`PdfPreview` API drift against the resolved package version if `flutter analyze` flags one.

- [ ] **Step 6: Commit**

```bash
git add lib/screens/reports/ lib/layouts/app_shell.dart
git commit -m "feat(reports): add ReportsScreen (appointments/revenue PDF tabs) and nav entry"
```

---

## Task 8: End-to-end verification + `GOALS.md` update

**Files:**
- Modify: `GOALS.md` (repo root, not under `ClinicNow/`) — matrix row 12 and the progress log.

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: nothing new — this task only verifies and records.

- [ ] **Step 1: Full backend build**

```bash
cd ClinicNow && dotnet build
```
Expected: 0 warnings/errors across the whole solution.

- [ ] **Step 2: Full desktop analyze + test**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter analyze && flutter test
cd ../clinicnow_mobile && flutter analyze && flutter test
```
Expected: "No issues found!" on both; existing tests still pass on both (mobile is untouched by this phase — a pure regression check).

- [ ] **Step 3: Live run + role-gating regression sweep**

Run the API (`dotnet run --project ClinicNow.API --urls "http://localhost:5235"`, or `docker compose up` if Docker is reachable in the execution environment) and repeat Task 4 Step 5's curl checks, plus confirm a Doctor token also gets `403` on all three endpoints (Doctor was never granted access per the design's access decision):

```bash
DOCTOR_TOKEN=$(curl -s -X POST http://localhost:5235/api/auth/login -H "Content-Type: application/json" -d '{"email":"doctor@clinicnow.test","password":"test"}' | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5235/api/Dashboard/summary -H "Authorization: Bearer $DOCTOR_TOKEN"
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5235/api/Reports/appointments-pdf?startDate=2026-01-01&endDate=2026-12-31" -H "Authorization: Bearer $DOCTOR_TOKEN"
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5235/api/Reports/revenue-pdf?startDate=2026-01-01&endDate=2026-12-31" -H "Authorization: Bearer $DOCTOR_TOKEN"
```
Expected: `403` on all three.

- [ ] **Step 4: Visual UI spot-check**

Run `clinicnow_desktop` against the live API, log in as `staff@clinicnow.test`/`test`, confirm: the "Početna" nav entry shows the real `DashboardScreen` (KPI cards + both charts render with real seeded numbers, not placeholder text); a Doctor login no longer sees a "Početna" or "Izvještaji" entry at all; the "Izvještaji" screen's two tabs each generate a real inline PDF preview via `PdfPreview` for a sensible date range, and its print icon opens a real print dialog (or at minimum doesn't crash — full physical-printer verification isn't required, matching this phase's own "no automated test project, verify live" convention already used for every prior phase's payment/UI work).

- [ ] **Step 5: Update `GOALS.md`**

Change matrix row 12 from `☐` to `☑`:

```
| 12 | ≥2 PDF reports; each list view has ≥1 search param | Appointments + Revenue (server-side PDF) | ☑ |
```

Append a dated entry to the Progress Log (§9) summarizing what Task 1-7 built and what was verified live in this task (build clean, role-gating confirmed against real Administrator/Staff/Doctor/Patient/anonymous requests, PDF byte output sanity-checked, Sarajevo-boundary spot-check result, and whether Docker or bare `dotnet run` was the verification level actually achieved — following the exact honesty convention every prior phase's log entry already uses for this distinction).

- [ ] **Step 6: Commit**

```bash
git add GOALS.md
git commit -m "docs(reports): mark Phase 9 reports/dashboard complete in GOALS.md"
```
