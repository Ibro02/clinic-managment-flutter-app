using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class DoctorSpecializationConfiguration : IEntityTypeConfiguration<DoctorSpecialization>
{
    public void Configure(EntityTypeBuilder<DoctorSpecialization> builder)
    {
        builder.HasKey(ds => new { ds.DoctorId, ds.SpecializationId });

        builder.HasOne(ds => ds.Doctor)
            .WithMany(d => d.DoctorSpecializations)
            .HasForeignKey(ds => ds.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ds => ds.Specialization)
            .WithMany()
            .HasForeignKey(ds => ds.SpecializationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Doctor 1 (Emir) -> Opća medicina (Specialization 1); Doctor 2 (Amila) -> Kardiologija (Specialization 4).
        builder.HasData(
            new DoctorSpecialization { DoctorId = 1, SpecializationId = 1 },
            new DoctorSpecialization { DoctorId = 2, SpecializationId = 4 });
    }
}
