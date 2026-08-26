using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A real, app-written signal (doc §3): "otvaranje detalja doktora ili
/// usluge, pretraga". Not a reference/codebook table (CLAUDE.md §6) and not
/// a pure M:N join - this is a genuine, growing event log, so it counts
/// toward the ≥10 non-reference tables (rulebook Part II §A).
///
/// <see cref="DoctorId"/>/<see cref="MedicalServiceId"/> are both nullable
/// because which of the two a row carries depends on the interaction type: a
/// <see cref="Model.Common.InteractionType.DoctorView"/> row carries exactly
/// the doctor and a <see cref="Model.Common.InteractionType.MedicalServiceView"/>
/// row exactly the service, while a <see cref="Model.Common.InteractionType.Search"/>
/// row may legitimately carry both at once (a search term that matched a
/// doctor *and* a service). Never both null, though - enforced in
/// <c>RecommenderService.LogInteractionAsync</c>, since a row with neither
/// can never feed into scoring (see RecommenderService.BuildInteractionHistoryRows).
/// </summary>
public class RecommenderInteraction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public InteractionType InteractionType { get; set; }

    public int? DoctorId { get; set; }

    public Doctor? Doctor { get; set; }

    public int? MedicalServiceId { get; set; }

    public MedicalService? MedicalService { get; set; }

    public DateTime DateTimeUtc { get; set; }
}
