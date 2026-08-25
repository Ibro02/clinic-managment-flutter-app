using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    private static readonly DateTime SeedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.Property(d => d.LicenseNumber).HasMaxLength(50);
        builder.Property(d => d.Bio).HasMaxLength(1000);

        builder.HasIndex(d => d.UserId).IsUnique();

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A doctor practices at exactly one clinic (1:1) - never independently
        // choosable when booking (that would let a caller pick a doctor and an
        // unrelated clinic). Restrict, not Cascade: a Location with doctors
        // assigned can't be deleted out from under them.
        builder.HasOne(d => d.Location)
            .WithMany()
            .HasForeignKey(d => d.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mirrors the two seeded doctor Users (Phase 1's doctor@clinicnow.test,
        // Phase 3's doctor2@clinicnow.test) so the seeded data demonstrates two
        // doctors with different specializations - and now different clinics too
        // (Doctor 1 -> Location 1 "Poliklinika Centar"/Sarajevo, Doctor 2 ->
        // Location 2 "Poliklinika Sunce"/Mostar, matching where their seeded
        // Phase 4 appointments already took place).
        builder.HasData(
            new Doctor
            {
                Id = 1,
                UserId = 3,
                LocationId = 1,
                LicenseNumber = "LKB-10023",
                Bio = "Doktor opće medicine sa 10 godina iskustva.",
                CreatedAtUtc = SeedCreatedAtUtc
            },
            new Doctor
            {
                Id = 2,
                UserId = 5,
                LocationId = 2,
                LicenseNumber = "LKB-10087",
                Bio = "Specijalista kardiologije.",
                CreatedAtUtc = SeedCreatedAtUtc
            });
    }
}
