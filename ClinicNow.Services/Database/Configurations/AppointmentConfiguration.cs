using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.Property(a => a.CancellationReason).HasMaxLength(500);

        // Every FK off Appointment is Restrict, never Cascade: an appointment is
        // never silently deleted as a side effect of deleting something it
        // references (rulebook Part II §A: "cascade only where it makes sense" -
        // here it never does, since Appointment itself is never hard-deleted at
        // all). Attempting to delete a referenced Doctor/Patient/MedicalService/
        // Location already surfaces a clean "referenced elsewhere" error via
        // BaseCRUDService's generic DbUpdateException guard - no extra code needed.
        // `.IsRequired(false)` here is not saying PatientId can be NULL (it can't -
        // it's a non-nullable int, enforced at the DB level regardless). It fixes
        // a real EF Core footgun: Patient has a global soft-delete query filter,
        // and EF treats a *required* relationship into a filtered entity as an
        // INNER JOIN for `Include` purposes - meaning every appointment belonging
        // to a soft-deleted patient would silently vanish from every query that
        // includes Patient (which is all of them, via IncludeAll), not just show
        // a null Patient. Appointments for a soft-deleted patient must stay fully
        // visible for the legally-retained record/audit trail (CLAUDE.md §5) -
        // this is what actually makes that true.
        builder.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Doctor).WithMany().HasForeignKey(a => a.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.MedicalService).WithMany().HasForeignKey(a => a.MedicalServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Location).WithMany().HasForeignKey(a => a.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.CreatedByUser).WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.DoctorId);
        builder.HasIndex(a => a.PatientId);
        builder.HasIndex(a => a.Status);

        // Seed data spans Completed/Cancelled (past) and Pending/Confirmed
        // (future, relative to this seed's fixed "today" of 2026-08-25) so every
        // status is demonstrable on a clean DB without needing to walk an
        // appointment through the state machine by hand first. Each seed row
        // gets exactly one matching AppointmentAuditLog reflecting its current
        // status (see AppointmentAuditLogConfiguration) rather than a full
        // synthetic history - the real multi-row history is what the actual
        // state machine produces once the app is used for real.
        builder.HasData(
            new Appointment
            {
                Id = 1, PatientId = 1, DoctorId = 1, MedicalServiceId = 1, LocationId = 1,
                StartUtc = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 8, 18, 9, 30, 0, DateTimeKind.Utc),
                Status = AppointmentStatus.Completed, CreatedByUserId = 2, CreatedAtUtc = Seed
            },
            new Appointment
            {
                Id = 2, PatientId = 2, DoctorId = 1, MedicalServiceId = 2, LocationId = 1,
                StartUtc = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 8, 20, 10, 30, 0, DateTimeKind.Utc),
                Status = AppointmentStatus.Cancelled, CancellationReason = "Pacijent se razbolio.",
                CreatedByUserId = 2, CreatedAtUtc = Seed
            },
            new Appointment
            {
                Id = 3, PatientId = 1, DoctorId = 2, MedicalServiceId = 5, LocationId = 2,
                StartUtc = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 8, 26, 9, 45, 0, DateTimeKind.Utc),
                Status = AppointmentStatus.Confirmed, CreatedByUserId = 4, CreatedAtUtc = Seed
            },
            new Appointment
            {
                Id = 4, PatientId = 2, DoctorId = 1, MedicalServiceId = 1, LocationId = 1,
                StartUtc = new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 8, 27, 8, 30, 0, DateTimeKind.Utc),
                Status = AppointmentStatus.Pending, CreatedByUserId = 2, CreatedAtUtc = Seed
            },
            new Appointment
            {
                Id = 5, PatientId = 1, DoctorId = 2, MedicalServiceId = 4, LocationId = 2,
                StartUtc = new DateTime(2026, 9, 2, 11, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 9, 2, 11, 15, 0, DateTimeKind.Utc),
                Status = AppointmentStatus.Pending, CreatedByUserId = 4, CreatedAtUtc = Seed
            });
    }
}
