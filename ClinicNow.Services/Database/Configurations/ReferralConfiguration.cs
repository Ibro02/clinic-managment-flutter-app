using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> builder)
    {
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(2000);

        // .IsRequired(false) on the navigation only - PatientId itself stays
        // NOT NULL. Same reasoning as LabFinding.Patient (review item C3): a
        // required navigation into a filtered (soft-deletable) entity becomes
        // an inner join for Include() - without this, an archived patient's
        // referral history would silently vanish instead of staying visible.
        builder.HasOne(r => r.Patient)
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ReferringDoctor)
            .WithMany()
            .HasForeignKey(r => r.ReferringDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        // A referral always documents the specific examination it was issued
        // during (review item C5: "doktor kreira uputnicu tokom pregleda") -
        // Appointment is never soft-deleted, so this navigation stays required.
        builder.HasOne(r => r.SourceAppointment)
            .WithMany()
            .HasForeignKey(r => r.SourceAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.TargetSpecialization)
            .WithMany()
            .HasForeignKey(r => r.TargetSpecializationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional - the referring doctor may name a specific specialist, or
        // leave the choice to the patient. A second relationship onto Doctor
        // alongside ReferringDoctor; EF tells them apart by their FK property.
        builder.HasOne(r => r.TargetDoctor)
            .WithMany()
            .HasForeignKey(r => r.TargetDoctorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional - only set once a booking is made "from" this referral
        // (review item C5's "continue to booking"). A second, independent
        // relationship onto Appointment alongside SourceAppointment - EF
        // disambiguates the two by their distinct FK properties.
        builder.HasOne(r => r.ResultingAppointment)
            .WithMany()
            .HasForeignKey(r => r.ResultingAppointmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => r.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Seeded Appointment 3 (Patient 1, Doctor 2 = doctor2@clinicnow.test,
        // User Id 5) - a different appointment than LabFindingConfiguration's
        // seed (Appointment 1), so the demo data covers two distinct
        // Phase 3 records instead of stacking both onto the same exam.
        builder.HasData(new Referral
        {
            Id = 1,
            PatientId = 1,
            ReferringDoctorId = 2,
            SourceAppointmentId = 3,
            TargetSpecializationId = 4, // Kardiologija
            Reason = "Povišen krvni pritisak i nepravilan puls - potrebna kardiološka evaluacija.",
            CreatedByUserId = 5,
            CreatedAtUtc = new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc)
        });
    }
}
