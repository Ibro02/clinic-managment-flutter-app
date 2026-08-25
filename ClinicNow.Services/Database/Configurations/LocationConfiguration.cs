using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.Property(l => l.Name).IsRequired().HasMaxLength(150);
        builder.Property(l => l.Address).IsRequired().HasMaxLength(250);

        builder.HasOne(l => l.City)
            .WithMany(c => c.Locations)
            .HasForeignKey(l => l.CityId)
            // Reference data (City) must not silently disappear a Location's FK -
            // deleting a City that's still referenced is blocked instead (rulebook
            // Part II §A: "cascade only where it makes sense").
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new Location { Id = 1, Name = "Poliklinika Centar", Address = "Ferhadija 12", CityId = 1 },
            new Location { Id = 2, Name = "Poliklinika Sunce", Address = "Bulevar narodne revolucije 5", CityId = 2 },
            new Location { Id = 3, Name = "Poliklinika Zdravlje", Address = "Slatina 3", CityId = 3 });
    }
}
