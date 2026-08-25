using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class MedicalServiceConfiguration : IEntityTypeConfiguration<MedicalService>
{
    public void Configure(EntityTypeBuilder<MedicalService> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(150);
        builder.HasIndex(s => s.Name).IsUnique();
        builder.Property(s => s.Description).HasMaxLength(500);
        // decimal(8,2): up to 999,999.99 - comfortably covers any clinic service
        // price while keeping an explicit, intentional precision (never let EF's
        // provider-default float precision silently apply to money - CLAUDE.md
        // "Architecture & Code Quality").
        builder.Property(s => s.Price).HasColumnType("decimal(8,2)");

        builder.HasData(
            new MedicalService { Id = 1, Name = "Opći pregled", Description = "Osnovni ljekarski pregled.", Price = 50.00m, DurationMinutes = 30 },
            new MedicalService { Id = 2, Name = "Dermatološki pregled", Description = "Pregled kod dermatologa.", Price = 80.00m, DurationMinutes = 30 },
            new MedicalService { Id = 3, Name = "Ultrazvuk", Description = "Ultrazvučni pregled.", Price = 60.00m, DurationMinutes = 20 },
            new MedicalService { Id = 4, Name = "Laboratorijske analize", Description = "Osnovne laboratorijske pretrage krvi.", Price = 40.00m, DurationMinutes = 15 },
            new MedicalService { Id = 5, Name = "Kardiološki pregled", Description = "Pregled kod kardiologa.", Price = 90.00m, DurationMinutes = 45 });
    }
}
