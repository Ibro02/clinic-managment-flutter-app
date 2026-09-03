namespace ClinicNow.Model.Common;

/// <summary>
/// Categorical "doba dana" feature used by the recommender's content-based
/// pipeline (recommender-dokumentacija.md §4). Bucketed over the <b>clinic-local</b>
/// hour: a patient who prefers Monday mornings means 08:00 in Sarajevo, and in
/// summer that instant is 06:00 UTC - bucketing the UTC hour labelled it "jutro"
/// only by luck and shifted category boundaries by an hour twice a year
/// (review item C1). The result is still independent of the server/Docker
/// timezone, because the clinic zone is fixed rather than machine-derived.
/// </summary>
public enum TimeOfDayBucket
{
    Morning = 0,   // [06:00, 12:00) clinic-local
    Afternoon = 1, // [12:00, 18:00) clinic-local
    Evening = 2    // everything else (evening/night), clinic-local
}

public static class TimeOfDayBucketExtensions
{
    /// <summary>
    /// Buckets a UTC instant by the hour the clinic's clock showed at that moment.
    /// Takes UTC in and converts internally on purpose: the previous signature
    /// bucketed whatever it was handed, which is exactly how the UTC-vs-local
    /// defect went unnoticed.
    /// </summary>
    public static TimeOfDayBucket ToClinicTimeOfDayBucket(this DateTime utcDateTime) =>
        ClinicTimeZone.ToLocal(utcDateTime).Hour switch
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
