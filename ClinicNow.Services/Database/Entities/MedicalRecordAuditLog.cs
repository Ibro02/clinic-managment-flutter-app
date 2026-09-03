using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One row per mutation of a <see cref="MedicalRecord"/> or one of its
/// <see cref="MedicalRecordEntry"/> rows - who, when (UTC), and what changed.
/// Mandatory per review item C11 ("sacuvati audit ko/kada/sta je promijenio
/// ili obrisao"). Same shape and role as <see cref="AppointmentAuditLog"/>,
/// applied to the karton instead of the booking state machine - not exposed
/// via any DTO/API, same as that one (a DB-level retention record, not a
/// UI feature nobody asked for).
/// </summary>
public class MedicalRecordAuditLog
{
    public int Id { get; set; }

    public int MedicalRecordId { get; set; }

    public MedicalRecord MedicalRecord { get; set; } = null!;

    public MedicalRecordAuditAction Action { get; set; }

    public int ActingUserId { get; set; }

    public User ActingUser { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Free-text summary of what changed (e.g. which entry, or that notes were replaced).</summary>
    public string? Description { get; set; }
}
