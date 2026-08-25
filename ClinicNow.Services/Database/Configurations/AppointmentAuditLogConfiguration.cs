using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class AppointmentAuditLogConfiguration : IEntityTypeConfiguration<AppointmentAuditLog>
{
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<AppointmentAuditLog> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(500);

        builder.HasOne(l => l.Appointment)
            .WithMany(a => a.AuditLogs)
            .HasForeignKey(l => l.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade); // a log row has no meaning without its appointment

        builder.HasOne(l => l.ActingUser)
            .WithMany()
            .HasForeignKey(l => l.ActingUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // One row per seeded Appointment (Configurations/AppointmentConfiguration.cs),
        // reflecting its current status only - see the reasoning there.
        builder.HasData(
            new AppointmentAuditLog { Id = 1, AppointmentId = 1, Status = AppointmentStatus.Completed, ActingUserId = 2, OccurredAtUtc = Seed, Description = "Termin završen." },
            new AppointmentAuditLog { Id = 2, AppointmentId = 2, Status = AppointmentStatus.Cancelled, ActingUserId = 2, OccurredAtUtc = Seed, Description = "Pacijent se razbolio." },
            new AppointmentAuditLog { Id = 3, AppointmentId = 3, Status = AppointmentStatus.Confirmed, ActingUserId = 4, OccurredAtUtc = Seed, Description = "Termin potvrđen." },
            new AppointmentAuditLog { Id = 4, AppointmentId = 4, Status = AppointmentStatus.Pending, ActingUserId = 2, OccurredAtUtc = Seed, Description = "Termin zakazan." },
            new AppointmentAuditLog { Id = 5, AppointmentId = 5, Status = AppointmentStatus.Pending, ActingUserId = 4, OccurredAtUtc = Seed, Description = "Termin zakazan." });
    }
}
