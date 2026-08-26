using ClinicNow.Model.Common;

namespace ClinicNow.Model.Requests;

/// <summary>
/// Logs one real interaction (doc §3). At least one of <see cref="DoctorId"/>/
/// <see cref="MedicalServiceId"/> is required - a row with neither can never
/// contribute to scoring (see RecommenderService.BuildInteractionHistoryRows),
/// so the service rejects it up front rather than silently storing a useless
/// signal (the exact anti-pattern doc §3 warns against).
/// </summary>
public class RecommenderInteractionRequest
{
    public InteractionType Type { get; set; }

    public int? DoctorId { get; set; }

    public int? MedicalServiceId { get; set; }
}
