using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class RecommenderInteractionConfiguration : IEntityTypeConfiguration<RecommenderInteraction>
{
    public void Configure(EntityTypeBuilder<RecommenderInteraction> builder)
    {
        // Restrict, not Cascade: an interaction log row is a historical fact
        // about what a user did - it must never be silently wiped out as a
        // side effect of deleting an unrelated row (same reasoning as every
        // other FK off Appointment - see AppointmentConfiguration).
        builder.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Doctor).WithMany().HasForeignKey(i => i.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.MedicalService).WithMany().HasForeignKey(i => i.MedicalServiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => i.DateTimeUtc);

        // All 6 rows use UserId=4 (patient@clinicnow.test / Patient Id=1) -
        // confirmed directly against ClinicNow.Services/Database/Configurations/UserConfiguration.cs's
        // HasData (Id=1 Administrator, 2 Staff, 3 Doctor, 4 Patient, 5 Doctor2)
        // and the AddPatientsAndDoctors migration, where Patient Id=2 (Amar
        // Šehić) has a NULL UserId - he's a staff-entered walk-in with no
        // login (CLAUDE.md §6), so he can never authenticate and can never
        // produce a RecommenderInteraction row (they're only ever written by
        // RecommenderService.LogInteractionAsync off an authenticated JWT).
        // Spans all three InteractionType values for the one patient who can
        // actually generate them, so the "signals actually used" requirement
        // is demonstrable on a clean DB without needing the app to be used
        // first.
        builder.HasData(
            new RecommenderInteraction { Id = 1, UserId = 4, InteractionType = InteractionType.DoctorView, DoctorId = 1, DateTimeUtc = new DateTime(2026, 8, 22, 9, 15, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 2, UserId = 4, InteractionType = InteractionType.MedicalServiceView, MedicalServiceId = 1, DateTimeUtc = new DateTime(2026, 8, 22, 9, 16, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 3, UserId = 4, InteractionType = InteractionType.Search, DoctorId = 2, DateTimeUtc = new DateTime(2026, 8, 23, 18, 40, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 4, UserId = 4, InteractionType = InteractionType.DoctorView, DoctorId = 1, DateTimeUtc = new DateTime(2026, 8, 24, 8, 5, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 5, UserId = 4, InteractionType = InteractionType.MedicalServiceView, MedicalServiceId = 2, DateTimeUtc = new DateTime(2026, 8, 21, 14, 0, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 6, UserId = 4, InteractionType = InteractionType.Search, MedicalServiceId = 5, DateTimeUtc = new DateTime(2026, 8, 24, 19, 30, 0, DateTimeKind.Utc) });
    }
}
