using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        // FCM registration tokens run to roughly 160-200 characters today, but
        // Google documents them as opaque and variable-length, so this is
        // generous rather than measured.
        builder.Property(t => t.Token).IsRequired().HasMaxLength(512);
        builder.Property(t => t.Platform).IsRequired().HasMaxLength(20);

        // One row per token, enforced by the database and not just by the
        // service: re-registering is the normal case (every login), so the
        // service upserts against this index rather than accumulating a new row
        // per sign-in.
        builder.HasIndex(t => t.Token).IsUnique();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // No seed data: a token only exists once a real device has registered
        // one, and a fabricated token would make every demo push fail against
        // FCM with an invalid-registration error.
    }
}
