using ClinicNow.Model.Localization;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    // BCrypt hash of the literal password "test" (work factor 12), generated once
    // offline via BCrypt.Net-Next and pasted here as a fixed string - HasData values
    // must be constant at model-build time, and re-hashing on every migration
    // build would produce a different (still valid, but needlessly churny) string
    // each time. Verified against "test" via BCrypt.Verify at login time regardless
    // of which salt originally produced it - see README "Planned test accounts".
    private const string SeedPasswordHash = "$2a$12$J0bbFz7zAKZNCj7tEQSFpeiMgyxOZEcNYLbRAmCDapqmvmJyc3C3i";

    // Fixed instant (not DateTime.UtcNow) so HasData produces a byte-identical
    // migration every time it's regenerated - required for HasData in general.
    private static readonly DateTime SeedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.PhoneNumber).HasMaxLength(30);

        // Opt-out, not opt-in: without the DB default, the migration that adds
        // this column would backfill every existing account with `false` and
        // silently switch off reminders for people who never asked for that.
        builder.Property(u => u.EmailRemindersEnabled).IsRequired().HasDefaultValue(true);

        // Two-letter code (PatientLanguage.Bosnian/English) - length 5 leaves
        // room for a region tag (e.g. "en-US") without a schema change, even
        // though nothing generates one today. Same "opt-out needs a DB default"
        // reasoning as EmailRemindersEnabled above: without it, adding this
        // column would backfill every existing account with an empty string
        // rather than the clinic's actual default language.
        builder.Property(u => u.PreferredLanguage).IsRequired().HasMaxLength(5).HasDefaultValue(PatientLanguage.Bosnian);

        // A BCrypt hash, same size as PasswordHash - never the code itself.
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(200);

        // Four demo accounts, one per role, credentials documented in README.md
        // ("Planned test accounts"): <role>@clinicnow.test / test.
        builder.HasData(
            new User
            {
                Id = 1,
                Email = "administrator@clinicnow.test",
                PasswordHash = SeedPasswordHash,
                FirstName = "Amina",
                LastName = "Administratorović",
                PhoneNumber = "+38761000001",
                IsActive = true,
                CreatedAtUtc = SeedCreatedAtUtc
            },
            new User
            {
                Id = 2,
                Email = "staff@clinicnow.test",
                PasswordHash = SeedPasswordHash,
                FirstName = "Selma",
                LastName = "Osoblje",
                PhoneNumber = "+38761000002",
                IsActive = true,
                CreatedAtUtc = SeedCreatedAtUtc
            },
            new User
            {
                Id = 3,
                Email = "doctor@clinicnow.test",
                PasswordHash = SeedPasswordHash,
                FirstName = "Emir",
                LastName = "Doktorović",
                PhoneNumber = "+38761000003",
                IsActive = true,
                CreatedAtUtc = SeedCreatedAtUtc
            },
            new User
            {
                Id = 4,
                Email = "patient@clinicnow.test",
                PasswordHash = SeedPasswordHash,
                FirstName = "Hana",
                LastName = "Pacijentić",
                PhoneNumber = "+38761000004",
                IsActive = true,
                CreatedAtUtc = SeedCreatedAtUtc
            },
            // Second demo doctor (Phase 3) - lets the seeded data demonstrate a
            // doctor with a different specialization than doctor@clinicnow.test.
            new User
            {
                Id = 5,
                Email = "doctor2@clinicnow.test",
                PasswordHash = SeedPasswordHash,
                FirstName = "Amila",
                LastName = "Kardiologić",
                PhoneNumber = "+38761000005",
                IsActive = true,
                CreatedAtUtc = SeedCreatedAtUtc
            });
    }
}
