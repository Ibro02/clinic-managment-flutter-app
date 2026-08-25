using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One row per lifecycle transition of an <see cref="Appointment"/> - who changed
/// it, to what status, when (UTC), and why. Mandatory per CLAUDE.md §8 ("every
/// appointment records who created/confirmed/cancelled/completed it"). This is a
/// main (non-reference) table in its own right, not just metadata.
/// </summary>
public class AppointmentAuditLog
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    public AppointmentStatus Status { get; set; }

    public int ActingUserId { get; set; }

    public User ActingUser { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Free-text note - the cancellation/rejection reason when applicable, or a short description otherwise.</summary>
    public string? Description { get; set; }
}
