# Phase 9 — Reports (PDF) & Dashboards Design

> Companion to `PLAN.md` Phase 9. This document is the validated design from brainstorming with Ibrahim on 2026-08-26; the implementation plan (written next, via `writing-plans`) argues from this doc.

## 1. Purpose

Give clinic staff two downloadable/printable PDF business reports (Appointments, Revenue) and a live dashboard of clinic KPIs — the two remaining graded reporting capabilities (`GOALS.md` matrix row 12). No new database tables: everything reads from data phases 0–8 already write (`Appointment`, `Payment`, `PaymentItem`, `Patient`, `Doctor`, `WorkingHours`, `ScheduleBlock`, `MedicalService`).

## 2. Key decisions (from brainstorming)

| Decision | Choice | Why |
|---|---|---|
| Mobile scope | **Desktop-only** | Mobile login already hard-rejects non-Patient accounts; every other staff-facing feature (patients, doctors, codebooks, appointment admin) is desktop-only too. No mobile changes this phase. |
| Revenue basis | **Collected** (real `Payment` rows, EUR, net of refunds) | Matches Phase 8's own source of truth; "accrued" (service price × Completed appointments, KM) would need a second, parallel notion of revenue this project doesn't otherwise track. |
| Report/dashboard access | **Administrator + Staff only** | Matches every other business/admin screen; Doctor keeps its own-schedule-only visibility. |
| Chart library | **`fl_chart`** | Free, BSD-licensed, no account/license step — fits a seminar project. |
| Print mechanism | **`printing` package → OS print dialog** | `Printing.layoutPdf(bytes)` prints directly from the API's PDF bytes, no temp file. |
| Report UX | **Inline `PdfPreview` widget** (from the same `printing` package) | Renders the actual PDF pages in-screen with print/share controls built in — one dependency serves preview, print, and (via its share/save action) download. |
| Dashboard timezone | **Fixed clinic timezone: `Europe/Sarajevo`** | Correct regardless of what timezone the API container or client machine happens to run in; a real single-clinic deployment has one timezone. |
| Weekly trend | **Daily appointment count, last 7 days (today back 6)** | Simple, standard activity-at-a-glance chart. |
| New-vs-existing patients | **Rolling 30-day window**: "new" = first-ever appointment falls inside the window; "existing" = had an appointment activity in the window but their first-ever appointment was earlier | Standard patient-acquisition metric, no extra config. |
| Appointments report layout | **Grouped sections per doctor**, each with a subtotal, grand total at the end | Reads like a real per-doctor activity report for a multi-doctor clinic. |
| "Available doctors" KPI | **On duty right now**: active (`User.IsActive`) doctors whose `WorkingHours` cover the current moment today and have no active `ScheduleBlock` right now | Answers "who could see a walk-in this minute," not just a roster count. |
| "Monthly revenue" KPI | **Current calendar month to date** (Europe/Sarajevo) | Resets each month; matches how a clinic owner naturally asks "how are we doing this month." |
| Dashboard placement | **Default landing screen** for Administrator/Staff after login | The nav rail's "Početna" entry is already a placeholder reserved for exactly this ("dashboard i moduli dolaze u narednim fazama" — `app_shell.dart`); Doctor/Patient landing behavior is unaffected. |

## 3. Backend architecture

Two new bespoke services under `ClinicNow.Services/Reports/` (not `BaseCRUDService` — neither is CRUD):

- **`IReportPdfService`/`ReportPdfService`** — builds both PDFs with **QuestPDF**. `QuestPDF.Settings.License = LicenseType.Community;` is set once in `Program.cs` at startup (required since QuestPDF 2023.12+, otherwise it throws at generation time).
- **`IDashboardService`/`DashboardService`** — computes the KPI aggregate. Cached briefly via `IMemoryCache` (short TTL, e.g. 60s) per CLAUDE.md's per-request-data caching rule, so rapid dashboard reloads don't re-run every aggregate query.

A small internal helper, **`ClinicTimeZone`** (static, `ClinicNow.Services/Common/` or similar), centralizes the Europe/Sarajevo conversion — `TimeZoneInfo.FindSystemTimeZoneById("Europe/Sarajevo")` (IANA id, resolved via ICU on both Windows and Linux under .NET 10) plus helpers for "today's local date range in UTC," "current local instant," and "start of this local calendar month in UTC." Every date/time computation in both services goes through this one helper — no ad-hoc `DateTime.Now`/`TimeZoneInfo` calls scattered across the code.

New **`ReportsController`** (`[Authorize(Roles = Roles.Administrator + "," + Roles.Staff)]`, matching `PaymentController`'s bespoke-controller pattern, not a generic base):

- `GET api/Reports/appointments-pdf?startDate={date}&endDate={date}&doctorId={int?}&statuses={AppointmentStatus[]?}` → `FileContentResult` (`application/pdf`, `Content-Disposition: attachment; filename=...`)
- `GET api/Reports/revenue-pdf?startDate={date}&endDate={date}` → `FileContentResult`
- `GET api/Dashboard/summary` → `DashboardSummaryDto` (JSON)

`startDate`/`endDate` are plain `DateOnly` query params (clinic-local calendar dates, not UTC instants) — the server converts each to a UTC range via `ClinicTimeZone` before querying. This keeps timezone math server-side and out of the Flutter client entirely, consistent with "store UTC, convert to local only for display/computation."

**Validation** (both PDF endpoints): `startDate <= endDate` and `(endDate - startDate) <= 366 days`, else `ValidationException` — a defensive cap against an unbounded query/report, since these aren't paginated list endpoints and have no other size limit.

## 4. Reports

### Appointments report

- **Filters**: `startDate`/`endDate` (required), `doctorId` (optional — omitted/null means "all doctors"), `statuses` (optional list — omitted/empty means "all statuses").
- **Query**: `Appointment`s whose `StartUtc` falls in the computed UTC range (matching the filters), `Include`d with `Doctor.User`, `Patient`, `MedicalService` — one query, grouped in-memory by `DoctorId` after materializing (EF `GroupBy` translated to SQL where possible; the per-doctor PDF section then just iterates the grouped result) to avoid N+1.
- **PDF layout**: one heading + table per doctor (e.g. "Dr. Ime Prezime — 12 termina"), rows = date/time, patient full name, service name, status (localized display name), sorted by date ascending within each doctor group; doctor subtotal row; grand total section at the end covering the whole period. Doctors with zero matching appointments in the period are omitted (not shown as an empty section).

### Revenue report

- **Filters**: `startDate`/`endDate` (required) only.
- **Query**: `Payment` rows with `Status` ∈ {`Paid`, `PartiallyRefunded`, `Refunded`} whose `PaidAtUtc` falls in the computed UTC range, `Include`d with `Items` (→ `MedicalService`) and `Refunds`. Net collected per payment = `AmountEur − sum(Refunds.AmountEur)`. Grouped by the service on `PaymentItem` (documented single-item-per-payment assumption carried over from Phase 8 — a payment with more than one item isn't currently possible to create, so no proportional-split logic is needed).
- **PDF layout**: one table, one row per service (name, payment count, net EUR total), sorted by net total descending; grand total row at the end.

## 5. Dashboard

`DashboardSummaryDto`:

```
TodayAppointmentsCount: int
ActivePatientsCount: int
AvailableDoctorsCount: int
MonthlyRevenueEur: decimal
WeeklyTrend: List<{ Date: DateOnly, AppointmentCount: int }>   // 7 entries, oldest to newest, today last
NewPatientsCount30d: int
ExistingPatientsCount30d: int
```

- **TodayAppointmentsCount** — `Appointment`s whose `StartUtc` falls within today's Europe/Sarajevo calendar day.
- **ActivePatientsCount** — `Patient` rows with `IsDeleted == false` (the existing soft-delete query filter already excludes deleted rows by default).
- **AvailableDoctorsCount** — `Doctor`s where `User.IsActive`, whose `WorkingHours` for today's `DayOfWeek` cover the current Europe/Sarajevo time-of-day, and who have no `ScheduleBlock` covering the current instant.
- **MonthlyRevenueEur** — same net-collected computation as the revenue report, windowed to `[start of this local calendar month, now]`.
- **WeeklyTrend** — `Appointment` count per local calendar day for the last 7 days (today back 6), by `StartUtc`.
- **NewPatientsCount30d / ExistingPatientsCount30d** — among patients with at least one appointment whose `StartUtc` falls in the last 30 days: a patient counts as "new" if that same window contains their chronologically-first-ever appointment; otherwise "existing."

## 6. Desktop UI

- **`DashboardScreen`** replaces the "Početna" placeholder in `app_shell.dart`, but only for Administrator/Staff — the "Početna" nav entry itself becomes conditional on `canManageCodebooks`-style role check (Administrator or Staff), same pattern already used for the "Šifrarnici" entry. A Doctor's nav rail simply no longer has a "Početna" entry at all; their list starts at "Pacijenti" exactly as it already behaves today for every other role-gated entry — no separate Doctor-facing placeholder or landing screen is introduced. KPI cards (today's appointments, active patients, available doctors, monthly revenue) across the top; `fl_chart` line or bar chart for the 7-day trend; a chart (bar or pie) for new-vs-existing patients. Pull-to-refresh / manual refresh button — no auto-polling needed (KPI dashboard, not a notification feed).
- **`ReportsScreen`** (new nav-rail item "Izvještaji", Administrator/Staff only, inserted after "Termini"): two sections (tabs or stacked cards) — "Izvještaj o terminima" (date range pickers, doctor dropdown with an "Svi doktori" option, status multi-select with an "Svi statusi" option) and "Izvještaj o prihodima" (date range pickers only). Each has a "Generiraj" button that calls its endpoint and renders the result inline via `PdfPreview` (print + share/save actions come from the widget itself); a separate explicit "Ispis" button is redundant with `PdfPreview`'s own print action, so `PdfPreview`'s built-in toolbar **is** the print/download UI — no duplicate buttons.

## 7. Error handling & verification

Same conventions as Phases 7/8: `ValidationException`/`BusinessException` → the existing `ExceptionFilter`, `ILogger<T>` on every PDF-generation or aggregate-query failure, never a leaked stack trace or raw exception. No backend automated test project in this codebase (established convention) — every task is verified live: build clean, migration N/A (no schema change), endpoints exercised against real seeded data, role-gating checked against real Administrator/Staff/Doctor/Patient tokens (expect 200/200/403/403), the Europe/Sarajevo boundary spot-checked with an appointment near local midnight, and generated PDF byte output sanity-checked (non-trivial size, correct `Content-Type`).

## 8. Out of scope for this phase

- Any mobile UI (deferred per §2 — desktop-only).
- Any new database table/entity — purely a read/aggregation layer over existing data.
- Exporting reports in any format other than PDF (no CSV/Excel).
- Scheduled/emailed reports — generation is on-demand only, triggered by a user action in the Reports screen.
- Changing how revenue or payments are recorded — this phase only reads Phase 8's existing `Payment`/`PaymentItem`/`PaymentRefund` data, never writes to it.
