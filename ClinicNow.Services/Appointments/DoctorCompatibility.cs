using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Appointments;

/// <summary>
/// The single definition of "may this doctor perform this service" (review item
/// C2): the doctor must hold the specialization the service belongs to, joined
/// through <c>DoctorSpecialization</c>.
///
/// Slot generation, server-side booking and the recommender all ask this same
/// question. Keeping the rule and its message in one place is what stops those
/// call sites drifting apart - which is exactly how the working-hours timezone
/// reading (item C1) ended up with two different interpretations of the same
/// column.
/// </summary>
public static class DoctorCompatibility
{
    public const string NotQualifiedMessage = "Odabrani doktor ne pruža odabranu uslugu.";

    /// <summary>True if <paramref name="doctorId"/> holds <paramref name="specializationId"/>.</summary>
    public static Task<bool> CanPerformAsync(
        ClinicNowContext context, int doctorId, int specializationId, CancellationToken cancellationToken) =>
        context.DoctorSpecializations.AnyAsync(
            ds => ds.DoctorId == doctorId && ds.SpecializationId == specializationId, cancellationToken);

    /// <summary>
    /// The same rule against an already-loaded graph, for callers that have the
    /// doctors in memory with <c>DoctorSpecializations</c> included (the
    /// recommender). Two shapes, one definition - never a second copy of the rule.
    /// </summary>
    public static bool CanPerform(Doctor doctor, MedicalService medicalService) =>
        doctor.DoctorSpecializations.Any(ds => ds.SpecializationId == medicalService.SpecializationId);
}
