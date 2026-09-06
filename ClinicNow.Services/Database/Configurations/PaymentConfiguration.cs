using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PayPalCaptureId).HasMaxLength(64);
        // decimal(8,2): same explicit money precision as MedicalService.Price -
        // never let EF's provider-default float precision silently apply.
        builder.Property(p => p.AmountEur).HasColumnType("decimal(8,2)");
        builder.Property(p => p.CapturedAmountEur).HasColumnType("decimal(8,2)");

        builder.HasOne(p => p.Appointment).WithMany(a => a.Payments).HasForeignKey(p => p.AppointmentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.AppointmentId);
        // Belt-and-suspenders for "at most one Paid payment per appointment"
        // (design doc §3) - a filtered unique index so the DB itself can never
        // end up with two Paid rows for the same appointment, even under a
        // race the service-layer check alone might not catch.
        builder.HasIndex(p => p.AppointmentId).HasFilter("[Status] = 1").IsUnique();

        builder.HasData(
            // CapturedAmountEur matches AmountEur on every seeded row: they stand
            // for payments that captured cleanly, which is what makes them useful
            // for demoing the refund ceiling (review item C13a).
            new Payment { Id = 1, AppointmentId = 1, AmountEur = 25.56m, CapturedAmountEur = 25.56m, Status = PaymentStatus.Paid, PayPalOrderId = "SEED-ORDER-0001", PayPalCaptureId = "SEED-CAPTURE-0001", CreatedAtUtc = Seed, PaidAtUtc = Seed },
            new Payment { Id = 2, AppointmentId = 2, AmountEur = 40.90m, CapturedAmountEur = 40.90m, Status = PaymentStatus.Refunded, PayPalOrderId = "SEED-ORDER-0002", PayPalCaptureId = "SEED-CAPTURE-0002", CreatedAtUtc = Seed, PaidAtUtc = Seed },
            new Payment { Id = 3, AppointmentId = 3, AmountEur = 46.02m, CapturedAmountEur = 46.02m, Status = PaymentStatus.PartiallyRefunded, PayPalOrderId = "SEED-ORDER-0003", PayPalCaptureId = "SEED-CAPTURE-0003", CreatedAtUtc = Seed, PaidAtUtc = Seed });
    }
}
