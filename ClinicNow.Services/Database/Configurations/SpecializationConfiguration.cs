using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class SpecializationConfiguration : IEntityTypeConfiguration<Specialization>
{
    public void Configure(EntityTypeBuilder<Specialization> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(s => s.Name).IsUnique();
        builder.Property(s => s.Description).HasMaxLength(500);

        builder.HasData(
            new Specialization { Id = 1, Name = "Opća medicina", Description = "Osnovni pregledi i savjetovanja." },
            new Specialization { Id = 2, Name = "Dermatologija", Description = "Bolesti kože, kose i noktiju." },
            new Specialization { Id = 3, Name = "Pedijatrija", Description = "Zdravstvena zaštita djece." },
            new Specialization { Id = 4, Name = "Kardiologija", Description = "Bolesti srca i krvnih sudova." },
            new Specialization { Id = 5, Name = "Ginekologija", Description = "Reproduktivno zdravlje žena." });
    }
}
