using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Reports;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Reports;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Reports;

/// <summary>
/// Review item C8: the revenue report gained a per-service filter, and both
/// reports gained a data shape the desktop charts. The point of the tests here
/// is that the filter narrows the figures correctly and that the chart data is
/// the same aggregation the PDF prints - a chart that disagreed with the
/// document beside it would be worse than no chart.
/// </summary>
public class ReportServiceTests
{
    // Deliberately a period the seed data does not touch, so the assertions are
    // about the payments this test creates rather than whatever the seed holds.
    private static readonly DateOnly PeriodStart = new(2027, 6, 1);
    private static readonly DateOnly PeriodEnd = new(2027, 6, 30);

    /// <summary>Two paid appointments for service A, one for service B, one of A's partly refunded.</summary>
    private static async Task<(ReportService Service, int ServiceAId, int ServiceBId)> BuildAsync(ClinicNowContext context)
    {
        var services = await context.MedicalServices.OrderBy(s => s.Id).Take(2).ToListAsync();
        var appointments = await context.Appointments.OrderBy(a => a.Id).Take(3).ToListAsync();
        Assert.Equal(2, services.Count);
        Assert.Equal(3, appointments.Count);

        var paidAt = ClinicTimeZone.LocalDateStartUtc(new DateOnly(2027, 6, 15));

        void AddPayment(int appointmentId, MedicalService service, decimal amountEur, decimal refundedEur)
        {
            var payment = new Payment
            {
                AppointmentId = appointmentId,
                AmountEur = amountEur,
                CapturedAmountEur = amountEur,
                Status = refundedEur > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Paid,
                PayPalOrderId = $"ORDER-{appointmentId}-{service.Id}",
                CreatedAtUtc = paidAt,
                PaidAtUtc = paidAt
            };
            payment.Items.Add(new PaymentItem
            {
                MedicalServiceId = service.Id,
                MedicalService = service,
                Description = service.Name,
                AmountEur = amountEur
            });
            if (refundedEur > 0)
            {
                payment.Refunds.Add(new PaymentRefund
                {
                    AmountEur = refundedEur,
                    Reason = "Test",
                    RefundedByUserId = 1,
                    RefundedAtUtc = paidAt
                });
            }
            context.Payments.Add(payment);
        }

        AddPayment(appointments[0].Id, services[0], 100m, 0m);
        AddPayment(appointments[1].Id, services[0], 50m, 20m); // net 30
        AddPayment(appointments[2].Id, services[1], 40m, 0m);
        await context.SaveChangesAsync();

        return (new ReportService(context), services[0].Id, services[1].Id);
    }

    [Fact]
    public async Task Revenue_data_is_net_of_refunds_and_grouped_by_service()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, serviceAId, _) = await BuildAsync(context);

        var data = await service.GetRevenueReportDataAsync(new RevenueReportFilter
        {
            StartDate = PeriodStart,
            EndDate = PeriodEnd
        });

        // 100 + (50 - 20) for service A, 40 for service B.
        Assert.Equal(170m, data.GrandTotalEur);

        var serviceAName = await context.MedicalServices.Where(s => s.Id == serviceAId).Select(s => s.Name).SingleAsync();
        var rowA = data.Rows.Single(r => r.ServiceName == serviceAName);
        Assert.Equal(130m, rowA.NetTotalEur);
        Assert.Equal(2, rowA.PaymentCount);
    }

    [Fact]
    public async Task Revenue_data_narrows_to_one_service_when_filtered()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _, serviceBId) = await BuildAsync(context);

        var data = await service.GetRevenueReportDataAsync(new RevenueReportFilter
        {
            StartDate = PeriodStart,
            EndDate = PeriodEnd,
            MedicalServiceId = serviceBId
        });

        var row = Assert.Single(data.Rows);
        Assert.Equal(40m, row.NetTotalEur);
        Assert.Equal(40m, data.GrandTotalEur);
    }

    [Fact]
    public async Task Revenue_report_rejects_an_unknown_service_rather_than_printing_an_empty_page()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _, _) = await BuildAsync(context);

        var filter = new RevenueReportFilter
        {
            StartDate = PeriodStart,
            EndDate = PeriodEnd,
            MedicalServiceId = 999_999
        };

        // A silently empty report reads as "no revenue", which is a very
        // different claim from "that service doesn't exist".
        var pdfFailure = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateRevenueReportAsync(filter));
        Assert.Contains("medicalServiceId", pdfFailure.Errors.Keys);

        // Asserted for the data endpoint too, because it was originally
        // rejected only on the PDF path - the chart happily rendered an empty
        // "0,00 EUR" for a service id that does not exist, which live testing
        // caught and this case now pins down.
        var dataFailure = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetRevenueReportDataAsync(filter));
        Assert.Contains("medicalServiceId", dataFailure.Errors.Keys);
    }

    [Fact]
    public async Task Appointments_data_counts_per_doctor_by_status()
    {
        using var context = TestContextFactory.CreateContext();
        var service = new ReportService(context);

        // A year around today - the widest the service permits, and enough to
        // cover the seeded appointments without pinning the test to a specific
        // seeded date. The expected count is derived from the same bounds.
        var today = ClinicTimeZone.NowLocal.Date;
        var startDate = DateOnly.FromDateTime(today.AddDays(-180));
        var endDate = DateOnly.FromDateTime(today.AddDays(180));

        var data = await service.GetAppointmentsReportDataAsync(new AppointmentsReportFilter
        {
            StartDate = startDate,
            EndDate = endDate
        });

        var startUtc = ClinicTimeZone.LocalDateStartUtc(startDate);
        var endUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(endDate);
        var expectedTotal = await context.Appointments.CountAsync(a => a.StartUtc >= startUtc && a.StartUtc < endUtc);
        Assert.True(expectedTotal > 0, "The seed should contain appointments around today for this to assert anything.");

        Assert.Equal(expectedTotal, data.TotalCount);
        Assert.Equal(expectedTotal, data.Rows.Sum(r => r.TotalCount));
        // Every appointment lands in exactly one status bucket, so the four
        // per-status counts must add back up to the row total.
        Assert.All(data.Rows, row => Assert.Equal(
            row.TotalCount,
            row.PendingCount + row.ConfirmedCount + row.CompletedCount + row.CancelledCount));
    }
}
