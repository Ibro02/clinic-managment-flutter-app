using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// The central, state-machined entity (CLAUDE.md §8). Never hard-deleted and never
/// has its <see cref="Status"/> written directly by a service - only through
/// <c>ClinicNow.Services.Appointments.AppointmentStateMachine</c> (Phase 4), which
/// also appends an <see cref="AppointmentAuditLog"/> row for every transition.
/// </summary>
public class Appointment
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public Patient Patient { get; set; } = null!;

    public int DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    public int MedicalServiceId { get; set; }

    public MedicalService MedicalService { get; set; } = null!;

    public int LocationId { get; set; }

    public Location Location { get; set; } = null!;

    public DateTime StartUtc { get; set; }

    /// <summary>Derived at creation from <c>StartUtc + MedicalService.DurationMinutes</c> - stored (not recomputed) so overlap queries can filter in SQL.</summary>
    public DateTime EndUtc { get; set; }

    public AppointmentStatus Status { get; set; }

    /// <summary>Set when cancelled - cancellation always requires a reason (rulebook Part II §G).</summary>
    public string? CancellationReason { get; set; }

    public int CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Set once a pre-appointment reminder email/notification has been sent for
    /// this appointment (<c>PreAppointmentReminderHostedService</c>), so the
    /// periodic scanner never sends the same reminder twice.
    /// </summary>
    public DateTime? ReminderSentAtUtc { get; set; }

    public ICollection<AppointmentAuditLog> AuditLogs { get; set; } = [];
}
