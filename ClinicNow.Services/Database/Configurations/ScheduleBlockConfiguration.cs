using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.Property(b => b.Reason).IsRequired().HasMaxLength(250);

        builder.HasOne(b => b.Doctor)
            .WithMany(d => d.ScheduleBlocks)
            .HasForeignKey(b => b.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);

        // One example block: Doctor 1 on vacation for a fixed, deterministic
        // week (HasData requires constant values, not DateTime.UtcNow-relative).
        builder.HasData(
            new ScheduleBlock
            {
                Id = 1,
                DoctorId = 1,
                StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 9, 7, 23, 59, 59, DateTimeKind.Utc),
                Reason = "Godišnji odmor"
            });
    }
}
