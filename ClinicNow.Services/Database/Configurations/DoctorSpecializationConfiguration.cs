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

        // Doctor 1 (Emir) -> Opća medicina (1) + Dermatologija (2);
        // Doctor 2 (Amila) -> Kardiologija (4) + Opća medicina (1).
        //
        // The second entry on each doctor is not decoration: once MedicalService
        // carries a SpecializationId (review item C2), the seeded appointments have
        // to satisfy the same compatibility rule the API now enforces. Appointment 2
        // is doctor 1 x "Dermatološki pregled" and appointment 5 is doctor 2 x
        // "Laboratorijske analize", so without these rows the seed would contradict
        // the constraint and two seeded services would be unbookable on a clean DB.
        builder.HasData(
            new DoctorSpecialization { DoctorId = 1, SpecializationId = 1 },
            new DoctorSpecialization { DoctorId = 1, SpecializationId = 2 },
            new DoctorSpecialization { DoctorId = 2, SpecializationId = 4 },
            new DoctorSpecialization { DoctorId = 2, SpecializationId = 1 });
    }
}
