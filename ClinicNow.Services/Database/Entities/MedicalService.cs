namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A billable clinical service (e.g. "Pregled kod dermatologa") - reference table/
/// codebook (CLAUDE.md §6). <c>Appointment</c> (Phase 4) links to exactly one of
/// these; <c>Payment</c>/<c>PaymentItem</c> (Phase 8) price off it. The price lives
/// here, server-side, as the single source of truth - never trust a client-supplied
/// amount (rulebook Part II §J).
/// </summary>
public class MedicalService
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// The specialization a doctor must hold to perform this service. Joined to
    /// doctors through <see cref="DoctorSpecialization"/>, this is what makes
    /// "can this doctor perform this service" answerable on the server instead of
    /// being a UI-only convention (review item C2).
    /// </summary>
    public int SpecializationId { get; set; }

    public Specialization Specialization { get; set; } = null!;

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>
    /// When true, booking this service requires an active (unused, non-archived)
    /// <see cref="Referral"/> targeting this service's <see cref="Specialization"/> -
    /// e.g. a surgical consultation the patient must first be referred to. Enforced
    /// server-side in <c>AppointmentService.ScheduleAsync</c>, never client-only.
    /// </summary>
    public bool IsReferralRequired { get; set; }
}
