using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    private static readonly DateTime SeedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.Property(r => r.Allergies).HasMaxLength(2000);
        builder.Property(r => r.MedicalNotes).HasMaxLength(4000);

        // The DB-level guarantee behind "every patient has exactly one medical
        // record" - not just an application-level convention.
        builder.HasIndex(r => r.PatientId).IsUnique();

        // .IsRequired(false) on the navigation only - PatientId itself stays
        // NOT NULL. Same root-cause fix as Appointment.Patient in Phase 4:
        // Patient carries a global soft-delete query filter, and EF treats a
        // *required* navigation into a filtered entity as an inner join for
        // Include() purposes - without this, a soft-deleted patient's medical
        // record would silently vanish from every query instead of just
        // failing an ownership check.
        builder.HasOne(r => r.Patient)
            .WithOne(p => p.MedicalRecord)
            .HasForeignKey<MedicalRecord>(r => r.PatientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // One record per seeded Patient (see PatientConfiguration) - created here
        // rather than left to lazily materialize, so both demo accounts have a
        // real, browsable medical file out of the box.
        builder.HasData(
            new MedicalRecord
            {
                Id = 1,
                PatientId = 1,
                Allergies = "Penicilin",
                MedicalNotes = "Bez hroničnih oboljenja.",
                CreatedAtUtc = SeedCreatedAtUtc,
                UpdatedAtUtc = SeedCreatedAtUtc
            },
            new MedicalRecord
            {
                Id = 2,
                PatientId = 2,
                Allergies = null,
                MedicalNotes = null,
                CreatedAtUtc = SeedCreatedAtUtc,
                UpdatedAtUtc = SeedCreatedAtUtc
            });
    }
}
