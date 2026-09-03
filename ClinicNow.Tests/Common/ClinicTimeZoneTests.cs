using ClinicNow.Model.Common;

namespace ClinicNow.Tests.Common;

/// <summary>
/// Guards the clinic-local ↔ UTC conversion that the whole booking flow depends on
/// (review item C1). WorkingHours.StartTime/EndTime are clinic wall-clock values:
/// 08:00 in Sarajevo is 06:00 UTC in summer but 07:00 UTC in winter, so a fixed
/// offset - or treating them as UTC outright, which is the bug being fixed here -
/// silently moves real appointments twice a year.
///
/// Europe/Sarajevo is CET (UTC+1) in winter and CEST (UTC+2) in summer, switching
/// on the last Sunday of March and of October.
/// </summary>
public class ClinicTimeZoneTests
{
    [Fact]
    public void ToUtc_InSummer_AppliesTheDaylightOffset()
    {
        var utc = ClinicTimeZone.ToUtc(new DateOnly(2026, 7, 15), new TimeOnly(8, 0));

        Assert.Equal(new DateTime(2026, 7, 15, 6, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void ToUtc_InWinter_AppliesTheStandardOffset()
    {
        var utc = ClinicTimeZone.ToUtc(new DateOnly(2026, 1, 15), new TimeOnly(8, 0));

        Assert.Equal(new DateTime(2026, 1, 15, 7, 0, 0, DateTimeKind.Utc), utc);
    }

    /// <summary>
    /// The regression that matters: the same wall-clock working hour must NOT map to
    /// the same UTC instant across a DST boundary. If these two are equal, the clinic
    /// day has silently shifted by an hour for half the year.
    /// </summary>
    [Fact]
    public void ToUtc_SameWallClockTime_MapsToDifferentUtcAcrossDstBoundary()
    {
        var summer = ClinicTimeZone.ToUtc(new DateOnly(2026, 7, 15), new TimeOnly(8, 0));
        var winter = ClinicTimeZone.ToUtc(new DateOnly(2026, 1, 15), new TimeOnly(8, 0));

        Assert.NotEqual(summer.TimeOfDay, winter.TimeOfDay);
    }

    [Theory]
    [InlineData(2026, 1, 15, 8, 0)]
    [InlineData(2026, 7, 15, 8, 0)]
    [InlineData(2026, 7, 15, 23, 45)]
    [InlineData(2026, 12, 31, 0, 0)]
    public void ToUtc_ThenBack_RoundTripsTheLocalDateAndTime(int year, int month, int day, int hour, int minute)
    {
        var date = new DateOnly(year, month, day);
        var time = new TimeOnly(hour, minute);

        var utc = ClinicTimeZone.ToUtc(date, time);

        Assert.Equal(date, ClinicTimeZone.LocalDateOf(utc));
        Assert.Equal(time, ClinicTimeZone.LocalTimeOf(utc));
    }

    /// <summary>
    /// 22:30 UTC on a Wednesday in summer is already 00:30 Thursday in Sarajevo.
    /// Booking matches WorkingHours by day-of-week, so reading the day off the UTC
    /// instant looks up the wrong doctor's schedule near midnight.
    /// </summary>
    [Fact]
    public void LocalDayOfWeekOf_UsesTheClinicDay_NotTheUtcDay()
    {
        var lateEveningUtc = new DateTime(2026, 7, 15, 22, 30, 0, DateTimeKind.Utc);

        Assert.Equal(DayOfWeek.Wednesday, lateEveningUtc.DayOfWeek);
        Assert.Equal(DayOfWeek.Thursday, ClinicTimeZone.LocalDayOfWeekOf(lateEveningUtc));
        Assert.Equal(new DateOnly(2026, 7, 16), ClinicTimeZone.LocalDateOf(lateEveningUtc));
    }

    /// <summary>
    /// Fall-back night: 02:30 local happens twice (once at +02:00, again at +01:00).
    /// Policy is the first occurrence, so a slot can never be generated for an instant
    /// that has already passed on the clinic's clock.
    /// </summary>
    [Fact]
    public void ToUtc_OnAnAmbiguousLocalTime_TakesTheFirstOccurrence()
    {
        var utc = ClinicTimeZone.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30));

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), utc);
    }

    /// <summary>
    /// Spring-forward night: 02:30 local never occurs - the clock jumps 02:00 → 03:00.
    /// Staff can still type it into a WorkingHours row, so this must resolve to a real
    /// instant rather than throwing an unhandled ArgumentException into a 500.
    /// </summary>
    [Fact]
    public void ToUtc_OnASkippedLocalTime_ShiftsForwardInsteadOfThrowing()
    {
        var utc = ClinicTimeZone.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30));

        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), utc);
        Assert.Equal(new TimeOnly(3, 30), ClinicTimeZone.LocalTimeOf(utc));
    }

    /// <summary>The new general conversion must agree with the existing day-boundary helper.</summary>
    [Fact]
    public void ToUtc_AtMidnight_MatchesLocalDateStartUtc()
    {
        var date = new DateOnly(2026, 7, 15);

        Assert.Equal(ClinicTimeZone.LocalDateStartUtc(date), ClinicTimeZone.ToUtc(date, TimeOnly.MinValue));
    }
}
