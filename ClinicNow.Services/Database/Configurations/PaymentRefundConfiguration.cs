using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        builder.Property(r => r.AmountEur).HasColumnType("decimal(8,2)");

        builder.HasOne(r => r.Payment).WithMany(p => p.Refunds).HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.RefundedByUser).WithMany().HasForeignKey(r => r.RefundedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Payment 2 (Cancelled appointment) -> refunded in full by Staff (User Id=2).
        // Payment 3 (Confirmed appointment) -> a partial goodwill refund by the
        // Administrator (User Id=1), leaving it PartiallyRefunded - both User
        // IDs confirmed against UserConfiguration.cs's HasData (Phase 7 Task 2
        // already verified: 1=Administrator, 2=Staff).
        builder.HasData(
            new PaymentRefund { Id = 1, PaymentId = 2, AmountEur = 40.90m, PayPalRefundId = "SEED-REFUND-0001", Reason = "Termin otkazan.", RefundedByUserId = 2, RefundedAtUtc = Seed },
            new PaymentRefund { Id = 2, PaymentId = 3, AmountEur = 15.00m, PayPalRefundId = "SEED-REFUND-0002", Reason = "Djelomični povrat na zahtjev pacijenta.", RefundedByUserId = 1, RefundedAtUtc = Seed });
    }
}
