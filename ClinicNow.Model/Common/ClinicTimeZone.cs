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

    /// <summary>The clinic-local calendar date an instant falls on.</summary>
    public static DateOnly LocalDateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>The clinic-local wall-clock time an instant falls on.</summary>
    public static TimeOnly LocalTimeOf(DateTime utc) => TimeOnly.FromDateTime(ToLocal(utc));

    /// <summary>
    /// The clinic-local day of week an instant falls on. Booking matches
    /// <c>WorkingHours.DayOfWeek</c>, so near midnight the UTC day and the clinic
    /// day differ and reading the day off the raw instant looks up the wrong
    /// schedule.
    /// </summary>
    public static DayOfWeek LocalDayOfWeekOf(DateTime utc) => ToLocal(utc).DayOfWeek;

    /// <summary>
    /// Converts a clinic-local wall-clock date + time into the UTC instant it
    /// denotes. This is the only sanctioned way to turn a <c>WorkingHours</c> row -
    /// or any other clinic wall-clock value - into an instant: 08:00 in Sarajevo is
    /// 06:00 UTC in summer but 07:00 UTC in winter, so treating those columns as
    /// UTC shifts real appointments by an hour for half the year.
    ///
    /// DST policy is decided here once so no call site has to think about it:
    /// <list type="bullet">
    /// <item><description><b>Skipped time</b> (spring forward - 02:30 on the last
    /// Sunday of March never occurs): shifted forward by the transition delta, so
    /// 02:30 resolves to 03:30. Staff can type such a value into a working-hours
    /// row, and throwing here would surface as a 500.</description></item>
    /// <item><description><b>Ambiguous time</b> (fall back - 02:30 on the last
    /// Sunday of October occurs twice): resolved to the <i>first</i> occurrence, so
    /// a generated slot is never already in the past on the clinic's clock.</description></item>
    /// </list>
    /// </summary>
    public static DateTime ToUtc(DateOnly localDate, TimeOnly localTime)
    {
        var local = DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(local))
        {
            local += TransitionDelta(local);
        }

        if (Zone.IsAmbiguousTime(local))
        {
            // The largest candidate offset is the daylight one, which yields the
            // earlier of the two instants - i.e. the first occurrence.
            var offset = Zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }

    /// <summary>The UTC instant corresponding to local midnight on <paramref name="date"/>.</summary>
    public static DateTime LocalDateStartUtc(DateOnly date) => ToUtc(date, TimeOnly.MinValue);

    /// <summary>How far the clock jumps at the transition covering <paramref name="local"/>.</summary>
    private static TimeSpan TransitionDelta(DateTime local)
    {
        var rule = Array.Find(Zone.GetAdjustmentRules(), r => local >= r.DateStart && local <= r.DateEnd);
        return rule?.DaylightDelta ?? TimeSpan.FromHours(1);
    }

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
