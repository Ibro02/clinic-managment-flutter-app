using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Reports;
using ClinicNow.Services.Database;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClinicNow.Services.Reports;

public class ReportService : IReportService
{
    private readonly ClinicNowContext _context;

    public ReportService(ClinicNowContext context)
    {
        _context = context;
    }

    public async Task<byte[]> GenerateAppointmentsReportAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default)
    {
        var appointments = await LoadAppointmentsAsync(filter, cancellationToken);

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
                                // Appointment.Patient can come back null at runtime for a
                                // soft-deleted patient: EF applies the global !IsDeleted query
                                // filter to Included navigations too, despite the `= null!`
                                // declaration on the entity. Render a fallback label instead of
                                // throwing (root-cause fix tracked separately).
                                var patientName = appointment.Patient is null
                                    ? "Obrisani pacijent"
                                    : $"{appointment.Patient.FirstName} {appointment.Patient.LastName}";
                                table.Cell().Element(RowCellStyle).Text(patientName);
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
        var data = await GetRevenueReportDataAsync(filter, cancellationToken);
        var rows = data.Rows;
        var grandTotal = data.GrandTotalEur;
        var serviceName = await ResolveServiceNameAsync(filter.MedicalServiceId, cancellationToken);

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
                    // A filtered report has to say so on the page: a printed
                    // total for one service is indistinguishable from a wrong
                    // total for the whole clinic once it leaves the screen.
                    column.Item().Text($"Usluga: {serviceName ?? "sve usluge"}").FontSize(10);
                });

                page.Content().PaddingTop(10).Column(column =>
                {
                    if (rows.Count == 0)
                    {
                        column.Item().Text(serviceName is null
                            ? "Nema naplaćenih uplata za odabrani period."
                            : $"Nema naplaćenih uplata za uslugu \"{serviceName}\" u odabranom periodu.");
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

    public async Task<AppointmentsReportData> GetAppointmentsReportDataAsync(AppointmentsReportFilter filter, CancellationToken cancellationToken = default)
    {
        var appointments = await LoadAppointmentsAsync(filter, cancellationToken);

        var rows = appointments
            .GroupBy(a => $"{a.Doctor.User.FirstName} {a.Doctor.User.LastName}")
            .Select(g => new AppointmentsReportRow
            {
                DoctorName = g.Key,
                PendingCount = g.Count(a => a.Status == AppointmentStatus.Pending),
                ConfirmedCount = g.Count(a => a.Status == AppointmentStatus.Confirmed),
                CompletedCount = g.Count(a => a.Status == AppointmentStatus.Completed),
                CancelledCount = g.Count(a => a.Status == AppointmentStatus.Cancelled),
                TotalCount = g.Count()
            })
            .OrderByDescending(r => r.TotalCount)
            .ThenBy(r => r.DoctorName)
            .ToList();

        return new AppointmentsReportData { Rows = rows, TotalCount = appointments.Count };
    }

    public async Task<RevenueReportData> GetRevenueReportDataAsync(RevenueReportFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateRange(filter.StartDate, filter.EndDate);

        // Validated here rather than only in the PDF path: an unknown service
        // id has to be rejected wherever it enters, or the chart shows a
        // confident, empty "0,00 EUR" that reads as "this service earned
        // nothing" instead of "you asked for a service that doesn't exist".
        await ResolveServiceNameAsync(filter.MedicalServiceId, cancellationToken);

        var startUtc = ClinicTimeZone.LocalDateStartUtc(filter.StartDate);
        var endUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(filter.EndDate);

        var query = _context.Payments
            .Include(p => p.Items).ThenInclude(i => i.MedicalService)
            .Include(p => p.Refunds)
            .Where(p => p.Status != PaymentStatus.Pending && p.PaidAtUtc != null
                && p.PaidAtUtc >= startUtc && p.PaidAtUtc < endUtc);

        if (filter.MedicalServiceId.HasValue)
        {
            // Narrowed in SQL, not after materializing every payment in the
            // period (rulebook Part II §D: filter at the DB). Safe alongside
            // the single-item assumption below - a payment matches iff its one
            // item is for this service.
            query = query.Where(p => p.Items.Any(i => i.MedicalServiceId == filter.MedicalServiceId.Value));
        }

        var payments = await query.ToListAsync(cancellationToken);

        // Single-item-per-payment assumption carried over from Phase 8 (design
        // doc §4 / Payments design doc §9) - the net amount is attributed
        // whole to that one item's service, never split proportionally.
        var rows = payments
            .SelectMany(p => p.Items.Select(item => new
            {
                item.MedicalServiceId,
                ServiceName = item.MedicalService.Name,
                NetAmountEur = p.AmountEur - p.Refunds.Sum(r => r.AmountEur)
            }))
            // The Any() above admits the whole payment; with more than one item
            // per payment this would also keep the other services' items, so
            // the filter is repeated on the flattened rows.
            .Where(x => !filter.MedicalServiceId.HasValue || x.MedicalServiceId == filter.MedicalServiceId.Value)
            .GroupBy(x => x.ServiceName)
            .Select(g => new RevenueReportRow
            {
                ServiceName = g.Key,
                PaymentCount = g.Count(),
                NetTotalEur = g.Sum(x => x.NetAmountEur)
            })
            .OrderByDescending(x => x.NetTotalEur)
            .ToList();

        return new RevenueReportData { Rows = rows, GrandTotalEur = rows.Sum(r => r.NetTotalEur) };
    }

    /// <summary>
    /// The appointments behind both the PDF and the per-doctor aggregate, so
    /// the two can never disagree about which appointments are in scope.
    /// </summary>
    private async Task<List<Database.Entities.Appointment>> LoadAppointmentsAsync(
        AppointmentsReportFilter filter, CancellationToken cancellationToken)
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
        // grouping by the callers runs in memory against that single result
        // set, never a per-doctor round trip (rulebook Part II §D: no N+1).
        return await query.OrderBy(a => a.StartUtc).ToListAsync(cancellationToken);
    }

    /// <summary>Null when the report covers every service - see the PDF header.</summary>
    private async Task<string?> ResolveServiceNameAsync(int? medicalServiceId, CancellationToken cancellationToken)
    {
        if (medicalServiceId is not int id)
        {
            return null;
        }

        return await _context.MedicalServices
            .Where(s => s.Id == id)
            .Select(s => s.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(new Dictionary<string, string[]>
            {
                ["medicalServiceId"] = ["Odabrana usluga ne postoji."]
            });
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
