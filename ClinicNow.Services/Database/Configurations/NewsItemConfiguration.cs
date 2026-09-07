using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    // A genuine, minimal, valid 1x1 transparent PNG (not a placeholder/null value) -
    // the rulebook requires seed data to actually include images where the domain
    // uses images, not just a nullable field left empty.
    private static readonly byte[] SeedImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        // Fixed-width hex from ContentHash.Compute - without this EF picks nvarchar(max).
        builder.Property(n => n.ContentHash).HasMaxLength(32);

        builder.Property(n => n.Title).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Text).IsRequired().HasMaxLength(4000);
        builder.Property(n => n.ImageContentType).HasMaxLength(100);

        builder.HasData(
            new NewsItem
            {
                Id = 1,
                Title = "Nove ordinacije od septembra",
                Text = "Poliklinika Sunce proširuje radno vrijeme kardiologije od 1. septembra.",
                ImageData = SeedImageBytes,
                ContentHash = Documents.ContentHash.Compute(SeedImageBytes),
                ImageContentType = "image/png",
                CreatedAtUtc = new DateTime(2026, 8, 15, 8, 0, 0, DateTimeKind.Utc)
            },
            new NewsItem
            {
                Id = 2,
                Title = "Online zakazivanje termina",
                Text = "Sada možete zakazati termin direktno iz mobilne aplikacije, bez poziva.",
                ImageData = null,
                ImageContentType = null,
                CreatedAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc)
            },
            new NewsItem
            {
                Id = 3,
                Title = "Sezonski pregledi",
                Text = "Preporučujemo redovne godišnje preglede - zakažite svoj termin na vrijeme.",
                ImageData = SeedImageBytes,
                ContentHash = Documents.ContentHash.Compute(SeedImageBytes),
                ImageContentType = "image/png",
                CreatedAtUtc = new DateTime(2026, 8, 23, 15, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
