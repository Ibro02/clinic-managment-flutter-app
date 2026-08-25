using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    private static readonly DateTime SeedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.Property(p => p.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.LastName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.PersonalIdNumber).HasMaxLength(20);
        builder.Property(p => p.PhoneNumber).HasMaxLength(30);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.Address).HasMaxLength(250);

        builder.HasIndex(p => p.PersonalIdNumber).IsUnique().HasFilter("[PersonalIdNumber] IS NOT NULL");
        builder.HasIndex(p => p.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Patient 1 mirrors the seeded patient@clinicnow.test User (Phase 1) - the
        // record a self-registered patient would have. Patient 2 is a walk-in
        // record with no login account, demonstrating staff can manage patients
        // who never use the mobile app.
        builder.HasData(
            new Patient
            {
                Id = 1,
                UserId = 4,
                FirstName = "Hana",
                LastName = "Pacijentić",
                PersonalIdNumber = "0101990175008",
                DateOfBirth = new DateOnly(1990, 1, 1),
                Gender = Gender.Female,
                PhoneNumber = "+38761000004",
                Email = "patient@clinicnow.test",
                Address = "Ferhadija 1, Sarajevo",
                CreatedAtUtc = SeedCreatedAtUtc,
                IsDeleted = false
            },
            new Patient
            {
                Id = 2,
                UserId = null,
                FirstName = "Amar",
                LastName = "Šehić",
                PersonalIdNumber = "1503985180012",
                DateOfBirth = new DateOnly(1985, 3, 15),
                Gender = Gender.Male,
                PhoneNumber = "+38762111222",
                Email = "amar.sehic@example.test",
                Address = "Zmaja od Bosne 10, Sarajevo",
                CreatedAtUtc = SeedCreatedAtUtc,
                IsDeleted = false
            });
    }
}
