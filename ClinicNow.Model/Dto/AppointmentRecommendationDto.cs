namespace ClinicNow.Model.Dto;

/// <summary>
/// One ranked, explainable suggestion (recommender-dokumentacija.md §6) - a
/// real free slot, never a client-side guess, always paired with a
/// human-readable <see cref="Reason"/> (rulebook Part II §K: never a raw ID -
/// every field here is a display name/value, not a bare FK).
/// </summary>
public class AppointmentRecommendationDto
{
    public int DoctorId { get; set; }

    public string DoctorName { get; set; } = string.Empty;

    public List<string> DoctorSpecializations { get; set; } = [];

    public int MedicalServiceId { get; set; }

    public string MedicalServiceName { get; set; } = string.Empty;

    public decimal MedicalServicePrice { get; set; }

    public int LocationId { get; set; }

    public string LocationName { get; set; } = string.Empty;

    /// <summary>The earliest real free slot for this doctor+service combination within the configured lookahead window.</summary>
    public DateTime SuggestedStartUtc { get; set; }

    /// <summary>Weighted cosine-similarity score in [0, 1] (0 for a pure popularity-fallback row).</summary>
    public double Score { get; set; }

    /// <summary>Short, human-readable explanation grounded in the real signals from doc §3 (never generic "Recommended for you").</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>True when this row came from the popularity fallback (doc §2.2) rather than the content-based model - lets the UI badge it differently if desired.</summary>
    public bool IsPopularityFallback { get; set; }
}
