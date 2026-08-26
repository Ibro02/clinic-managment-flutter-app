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
