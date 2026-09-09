using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
{
    public void Configure(EntityTypeBuilder<Diagnosis> builder)
    {
        builder.Property(d => d.Code).IsRequired().HasMaxLength(10);
        builder.HasIndex(d => d.Code).IsUnique();

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);

        // Restrict, not Cascade: deleting a specialization must never silently
        // take diagnoses (and through them, medical-record entries) with it.
        // SpecializationService's delete already refuses while anything
        // references it - this is the database-level backstop.
        builder.HasOne(d => d.SuggestedSpecialization)
            .WithMany()
            .HasForeignKey(d => d.SuggestedSpecializationId)
            .OnDelete(DeleteBehavior.Restrict);

        // A small, genuine ICD-10 subset covering the seeded specializations, so
        // the dropdown is usable on a clean database (rulebook §3.1: the seed
        // must contain enough data to actually test the feature).
        builder.HasData(
            new Diagnosis { Id = 1, Code = "Z00.0", Name = "Opća kontrola bez nalaza", SuggestedSpecializationId = 1 },
            new Diagnosis { Id = 2, Code = "J06.9", Name = "Akutna infekcija gornjih disajnih puteva", SuggestedSpecializationId = 1 },
            new Diagnosis { Id = 3, Code = "I10", Name = "Esencijalna (primarna) hipertenzija", SuggestedSpecializationId = 4 },
            new Diagnosis { Id = 4, Code = "I48", Name = "Atrijalna fibrilacija i treperenje", SuggestedSpecializationId = 4 },
            new Diagnosis { Id = 5, Code = "L20.9", Name = "Atopijski dermatitis", SuggestedSpecializationId = 2 },
            new Diagnosis { Id = 6, Code = "L70.0", Name = "Acne vulgaris", SuggestedSpecializationId = 2 },
            new Diagnosis { Id = 7, Code = "J45.9", Name = "Astma", SuggestedSpecializationId = 3 },
            new Diagnosis { Id = 8, Code = "N94.6", Name = "Dismenoreja", SuggestedSpecializationId = 5 },
            new Diagnosis { Id = 9, Code = "E11.9", Name = "Dijabetes melitus tip 2", SuggestedSpecializationId = 1 },
            new Diagnosis { Id = 10, Code = "R51", Name = "Glavobolja", SuggestedSpecializationId = 1 },
            new Diagnosis { Id = 11, Code = "Z00.1", Name = "Rutinska pedijatrijska kontrola", SuggestedSpecializationId = 3 },
            new Diagnosis { Id = 12, Code = "E03.9", Name = "Hipotireoza", SuggestedSpecializationId = 1 },
            new Diagnosis { Id = 13, Code = "I25.9", Name = "Hronična ishemijska bolest srca", SuggestedSpecializationId = 4 });
    }
}
