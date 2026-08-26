namespace ClinicNow.Model.Common;

/// <summary>
/// Categorical "doba dana" feature used by the recommender's content-based
/// pipeline (recommender-dokumentacija.md §4). Bucketed over the UTC hour -
/// never local time, so the result is identical regardless of server/Docker
/// timezone (doc §3: "izvođenje ... doba dana radi se nad UTC vrijednostima").
/// </summary>
public enum TimeOfDayBucket
{
    Morning = 0,   // [06:00, 12:00) UTC
    Afternoon = 1, // [12:00, 18:00) UTC
    Evening = 2    // [18:00, 06:00) UTC
}

public static class TimeOfDayBucketExtensions
{
    public static TimeOfDayBucket ToTimeOfDayBucket(this DateTime utcDateTime) => utcDateTime.Hour switch
    {
        >= 6 and < 12 => TimeOfDayBucket.Morning,
        >= 12 and < 18 => TimeOfDayBucket.Afternoon,
        _ => TimeOfDayBucket.Evening
    };

    /// <summary>Bosnian display label, mirroring <c>AppointmentStatusExtensions.ToDisplayName</c>.</summary>
    public static string ToDisplayName(this TimeOfDayBucket bucket) => bucket switch
    {
        TimeOfDayBucket.Morning => "ujutro",
        TimeOfDayBucket.Afternoon => "poslijepodne",
        TimeOfDayBucket.Evening => "uveče",
        _ => bucket.ToString()
    };
}
