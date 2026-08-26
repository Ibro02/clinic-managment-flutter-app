namespace ClinicNow.Model.Configuration;

/// <summary>
/// Recommender tuning, sourced from `.env` (doc §7: "putanje i parametri ...
/// čitaju se iz konfiguracije, a ne hardkodiraju u kodu"). Used only by
/// <c>ClinicNow.Services.Recommender.RecommenderService</c>.
/// </summary>
public class RecommenderOptions : EnvOptionsBase
{
    /// <summary>Top-N suggestions returned per request (doc §5.2, default 5).</summary>
    public int TopN { get; }

    /// <summary>Lookback window (days) for the popularity-based cold-start fallback (doc §2.2, default 90).</summary>
    public int PopularityWindowDays { get; }

    /// <summary>How many days ahead of "today" candidate free slots are searched for each (Doctor, MedicalService) pair.</summary>
    public int CandidateLookaheadDays { get; }

    /// <summary>Filesystem path the trained ML.NET model is serialized to/loaded from (doc §7).</summary>
    public string ModelPath { get; }

    /// <summary>How long a trained model stays cached before the next request that needs it retrains in-line, picking up catalog changes (doc §7: "model se osvježava periodično"). There is no background retrain - the refresh happens on the first use after this interval elapses.</summary>
    public int RetrainIntervalMinutes { get; }

    public RecommenderOptions()
    {
        TopN = GetOrDefault("RECOMMENDER_TOP_N", 5);
        PopularityWindowDays = GetOrDefault("RECOMMENDER_POPULARITY_WINDOW_DAYS", 90);
        CandidateLookaheadDays = GetOrDefault("RECOMMENDER_CANDIDATE_LOOKAHEAD_DAYS", 14);
        ModelPath = GetOrDefault("RECOMMENDER_MODEL_PATH", "recommender-model.zip");
        RetrainIntervalMinutes = GetOrDefault("RECOMMENDER_RETRAIN_INTERVAL_MINUTES", 30);
    }
}
