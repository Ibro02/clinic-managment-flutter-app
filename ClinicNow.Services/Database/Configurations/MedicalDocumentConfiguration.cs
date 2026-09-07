using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class MedicalDocumentConfiguration : IEntityTypeConfiguration<MedicalDocument>
{
    // A genuine, minimal, valid single-page PDF (not a placeholder) - starts with
    // the real %PDF magic bytes so it also demonstrates the service's own
    // magic-byte check would accept it, consistent with the rulebook's "seed data
    // must actually include real files" expectation (already applied to news
    // images in migration AddNotificationsAndNews).
    private static readonly byte[] SeedPdfBytes = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n" +
        "xref\n0 4\n0000000000 65535 f \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n0\n%%EOF");

    public void Configure(EntityTypeBuilder<MedicalDocument> builder)
    {
        // Fixed-width hex from ContentHash.Compute - without this EF picks nvarchar(max).
        builder.Property(d => d.ContentHash).HasMaxLength(32);

        builder.Property(d => d.FileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Description).HasMaxLength(2000);

        // Never cascade-delete a legally-retained record's files just because
        // the uploading staff account is later removed (there is no user
        // delete yet, but this stays consistent with every other FK onto
        // User in this schema - see e.g. Appointment.CreatedByUser).
        builder.HasOne(d => d.UploadedByUser)
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // .IsRequired(false) on the navigation only - PatientId itself stays
        // NOT NULL. Same root-cause fix as Appointment.Patient/MedicalRecord.Patient
        // (review item C3): Patient carries a global soft-delete query filter, and
        // EF treats a *required* navigation into a filtered entity as an inner
        // join for Include() purposes - without this, an archived patient's
        // documents would silently vanish from every query (worse than a null
        // reference: not an error, just gone) instead of failing an ownership
        // check while staying visible to staff who look them up directly.
        builder.HasOne(d => d.Patient)
            .WithMany()
            .HasForeignKey(d => d.PatientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Patient Id=1 is the self-registered patient@clinicnow.test (see
        // PatientConfiguration); UploadedByUserId=2 is staff@clinicnow.test (see
        // the identity seed in migration AddIdentity) - so the demo account can
        // log in on mobile and immediately see a real downloadable document.
        builder.HasData(new MedicalDocument
        {
            Id = 1,
            PatientId = 1,
            FileName = "nalaz-krvna-slika.pdf",
            ContentType = "application/pdf",
            FileData = SeedPdfBytes,
            FileSizeBytes = SeedPdfBytes.LongLength,
            ContentHash = Documents.ContentHash.Compute(SeedPdfBytes),
            Description = "Nalaz kompletne krvne slike - uredni parametri.",
            UploadedByUserId = 2,
            CreatedAtUtc = new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc),
            IsDeleted = false
        });
    }
}
