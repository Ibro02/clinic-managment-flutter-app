using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class LabFindingConfiguration : IEntityTypeConfiguration<LabFinding>
{
    // A genuine, minimal, valid single-page PDF (not a placeholder) - same
    // reasoning as MedicalDocumentConfiguration's seed: it also demonstrates
    // FileValidation's own magic-byte check would accept it.
    private static readonly byte[] SeedPdfBytes = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n" +
        "xref\n0 4\n0000000000 65535 f \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n0\n%%EOF");

    public void Configure(EntityTypeBuilder<LabFinding> builder)
    {
        builder.Property(f => f.Result).IsRequired().HasMaxLength(2000);
        builder.Property(f => f.FileName).IsRequired().HasMaxLength(260);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(100);

        // Never cascade-delete a legally-retained finding just because the
        // entering staff account is later removed - same reasoning as
        // MedicalDocument.UploadedByUser.
        builder.HasOne(f => f.EnteredByUser)
            .WithMany()
            .HasForeignKey(f => f.EnteredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A finding always belongs to the appointment it was produced for
        // (review item C4's actual requirement) - Appointment is never
        // soft-deleted, so this navigation stays required.
        builder.HasOne(f => f.Appointment)
            .WithMany()
            .HasForeignKey(f => f.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // .IsRequired(false) on the navigation only - PatientId itself stays
        // NOT NULL. Same reasoning as MedicalDocument.Patient (review item C3):
        // Patient carries a global soft-delete query filter, and a *required*
        // navigation into a filtered entity becomes an inner join for
        // Include() - without this, an archived patient's findings would
        // silently vanish instead of staying visible with a null Patient.
        builder.HasOne(f => f.Patient)
            .WithMany()
            .HasForeignKey(f => f.PatientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Appointment Id=1 (doctor@clinicnow.test, Completed, Patient 1) - same
        // seeded appointment MedicalRecordEntry's seed already ties its entry
        // to, so the demo doctor account has one real finding to show alongside
        // it on both desktop (from the appointment row) and mobile (patient's
        // own documentation).
        builder.HasData(new LabFinding
        {
            Id = 1,
            PatientId = 1,
            AppointmentId = 1,
            Result = "Kompletna krvna slika - uredni parametri.",
            FileName = "nalaz-kks.pdf",
            ContentType = "application/pdf",
            FileData = SeedPdfBytes,
            FileSizeBytes = SeedPdfBytes.LongLength,
            EnteredByUserId = 3,
            CreatedAtUtc = new DateTime(2026, 8, 18, 9, 30, 0, DateTimeKind.Utc),
            IsDeleted = false
        });
    }
}
