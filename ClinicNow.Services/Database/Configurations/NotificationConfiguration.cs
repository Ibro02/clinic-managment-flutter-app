using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Title).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Text).IsRequired().HasMaxLength(2000);

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed one demo notification per role's demo account (User Ids 1-4 from
        // migration AddIdentity: administrator/staff/doctor/patient) so the
        // notification bell/list is demoable on a clean DB.
        builder.HasData(
            new Notification
            {
                Id = 1,
                UserId = 4, // patient@clinicnow.test
                Title = "Dobrodošli u ClinicNow",
                Text = "Vaš nalog je uspješno kreiran. Zakažite svoj prvi termin iz aplikacije.",
                IsRead = false,
                CreatedAtUtc = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc)
            },
            new Notification
            {
                Id = 2,
                UserId = 3, // doctor@clinicnow.test
                Title = "Novi termin zakazan",
                Text = "Pacijent Amina Selimović je zakazao/la termin za pregled.",
                IsRead = true,
                CreatedAtUtc = new DateTime(2026, 8, 21, 10, 30, 0, DateTimeKind.Utc),
                ReadAtUtc = new DateTime(2026, 8, 21, 11, 0, 0, DateTimeKind.Utc)
            },
            new Notification
            {
                Id = 3,
                UserId = 2, // staff@clinicnow.test
                Title = "Podsjetnik: nadolazeći termini",
                Text = "Provjerite raspored za sutra - nekoliko termina čeka potvrdu.",
                IsRead = false,
                CreatedAtUtc = new DateTime(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
