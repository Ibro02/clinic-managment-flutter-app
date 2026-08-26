namespace ClinicNow.Model.Common;

/// <summary>
/// Centralizes the fixed clinic-local timezone (Europe/Sarajevo) so
/// "today"/"this week"/"this month" are computed consistently regardless of
/// what timezone the API container or a client machine happens to run in
/// (design doc §2/§3). .NET 6+ resolves IANA ids like this one on Windows as
/// well as Linux, so this works unchanged in Docker and on a Windows dev
/// machine. Every dashboard/report date computation goes through this one
/// class - no ad-hoc DateTime.Now/TimeZoneInfo calls elsewhere.
/// </summary>
public static class ClinicTimeZone
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sarajevo");

    /// <summary>The current instant, expressed in clinic-local time.</summary>
    public static DateTime NowLocal => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Converts a UTC instant to clinic-local time.</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>The UTC instant corresponding to local midnight on <paramref name="date"/>.</summary>
    public static DateTime LocalDateStartUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Zone);

    /// <summary>
    /// The UTC instant for the start of the local calendar day AFTER
    /// <paramref name="date"/> - an exclusive upper bound, so a range query
    /// is always `start &lt;= x &amp;&amp; x &lt; LocalDateEndExclusiveUtc(date)`,
    /// never an off-by-one around midnight.
    /// </summary>
    public static DateTime LocalDateEndExclusiveUtc(DateOnly date) => LocalDateStartUtc(date.AddDays(1));

    /// <summary>The UTC instant for the start of the current local calendar month.</summary>
    public static DateTime StartOfThisMonthUtc()
    {
        var today = DateOnly.FromDateTime(NowLocal);
        return LocalDateStartUtc(new DateOnly(today.Year, today.Month, 1));
    }
}
