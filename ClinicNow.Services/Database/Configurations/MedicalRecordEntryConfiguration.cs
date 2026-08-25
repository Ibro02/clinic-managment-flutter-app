using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class MedicalRecordEntryConfiguration : IEntityTypeConfiguration<MedicalRecordEntry>
{
    public void Configure(EntityTypeBuilder<MedicalRecordEntry> builder)
    {
        builder.Property(e => e.Treatment).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(2000);

        builder.HasOne(e => e.MedicalRecord)
            .WithMany(r => r.Entries)
            .HasForeignKey(e => e.MedicalRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Doctor Id (User) 3 = doctor@clinicnow.test, matching the identity seed.
        builder.HasData(new MedicalRecordEntry
        {
            Id = 1,
            MedicalRecordId = 1,
            EntryDate = new DateOnly(2026, 8, 10),
            Treatment = "Redovni pregled",
            Description = "Opći pregled bez nalaza. Preporučena kontrola za 6 mjeseci.",
            CreatedByUserId = 3,
            CreatedAtUtc = new DateTime(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc)
        });
    }
}
