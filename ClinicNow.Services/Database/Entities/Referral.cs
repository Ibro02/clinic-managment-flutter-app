namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A specialist referral ("uputnica") - review item C5. A real, standalone
/// business record (the reviewer explicitly rejects a plain note in the
/// karton as a substitute), created by a doctor during an examination
/// (<see cref="SourceAppointment"/>) and pointing at the specialization the
/// patient should be seen by next - never a specific pre-chosen doctor, since
/// booking with "the appropriate specialist" happens as a separate later
/// step (the client resolves a matching doctor via
/// <see cref="Doctor.DoctorSpecialization"/>/<c>Doctor.SpecializationIds</c>,
/// same relation review item C2 already established).
///
/// Permanent for Doctor/Staff - matches "stays part of the medical history" -
/// but <see cref="ISoftDelete"/> so it can be archived: either an
/// Administrator removes a mistaken entry directly
/// (<c>ReferralService.DeleteAsync</c>), or it archives itself automatically
/// once <see cref="ResultingAppointment"/> reaches a terminal state
/// (<c>BaseAppointmentState.ArchiveResultingReferralIfAnyAsync</c>). Either
/// way it is a soft-delete, never a physical removal - "Arhiva" in both
/// Flutter clients is a separate *view* over this same data, not a hidden one.
///
/// <see cref="PatientId"/> and <see cref="ReferringDoctorId"/> are both
/// derived server-side from <see cref="SourceAppointment"/>
/// (<c>ReferralService.CreateAsync</c>), never taken as separate
/// client-trusted inputs - same reasoning as <c>LabFinding.PatientId</c>
/// (review item C4).
/// </summary>
public class Referral : ISoftDelete
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    /// <summary>Optional - a soft-deleted (archived) patient's referral history must stay readable, same reasoning as <c>LabFinding.Patient</c> (review item C3).</summary>
    public Patient? Patient { get; set; }

    public int ReferringDoctorId { get; set; }
    public Doctor ReferringDoctor { get; set; } = null!;

    public int SourceAppointmentId { get; set; }
    public Appointment SourceAppointment { get; set; } = null!;

    public int TargetSpecializationId { get; set; }
    public Specialization TargetSpecialization { get; set; } = null!;

    /// <summary>The diagnosis/reason for the referral (e.g. "Sumnja na aritmiju, potrebna kardiološka evaluacija.").</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Set once a patient books an appointment "from" this referral
    /// (<c>AppointmentService.ScheduleAsync</c>'s <c>ReferralId</c> handling).
    /// A referral with this already set can never be used for a second
    /// booking - checked there, not just left to client-side UI hiding.
    /// </summary>
    public int? ResultingAppointmentId { get; set; }
    public Appointment? ResultingAppointment { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
