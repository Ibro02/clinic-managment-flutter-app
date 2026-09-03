using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class MedicalRecordAuditLogConfiguration : IEntityTypeConfiguration<MedicalRecordAuditLog>
{
    private static readonly DateTime Seed = new(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<MedicalRecordAuditLog> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(500);

        builder.HasOne(l => l.MedicalRecord)
            .WithMany(r => r.AuditLogs)
            .HasForeignKey(l => l.MedicalRecordId)
            .OnDelete(DeleteBehavior.Cascade); // a log row has no meaning without its karton

        builder.HasOne(l => l.ActingUser)
            .WithMany()
            .HasForeignKey(l => l.ActingUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matches the seeded MedicalRecordEntry Id=1 (Configurations/MedicalRecordEntryConfiguration.cs),
        // created by the same doctor at the same instant - same reasoning as AppointmentAuditLogConfiguration's seed.
        builder.HasData(new MedicalRecordAuditLog
        {
            Id = 1,
            MedicalRecordId = 1,
            Action = MedicalRecordAuditAction.EntryAdded,
            ActingUserId = 3,
            OccurredAtUtc = Seed,
            Description = "Unos dodan: Redovni pregled."
        });
    }
}
