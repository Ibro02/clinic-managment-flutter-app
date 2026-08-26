using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PaymentItemConfiguration : IEntityTypeConfiguration<PaymentItem>
{
    public void Configure(EntityTypeBuilder<PaymentItem> builder)
    {
        builder.Property(i => i.Description).HasMaxLength(200).IsRequired();
        builder.Property(i => i.AmountEur).HasColumnType("decimal(8,2)");

        builder.HasOne(i => i.Payment).WithMany(p => p.Items).HasForeignKey(i => i.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.MedicalService).WithMany().HasForeignKey(i => i.MedicalServiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new PaymentItem { Id = 1, PaymentId = 1, MedicalServiceId = 1, Description = "Opći pregled", AmountEur = 25.56m },
            new PaymentItem { Id = 2, PaymentId = 2, MedicalServiceId = 2, Description = "Dermatološki pregled", AmountEur = 40.90m },
            new PaymentItem { Id = 3, PaymentId = 3, MedicalServiceId = 5, Description = "Kardiološki pregled", AmountEur = 46.02m });
    }
}
