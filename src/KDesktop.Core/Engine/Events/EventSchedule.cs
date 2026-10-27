using KDesktop.Core.Model;

namespace KDesktop.Core.Engine.Events;

/// <summary>
/// Time maths of «a una hora»: the next time it fires and the times missed while K-Desktop was closed or the PC
/// asleep. Everything is UTC outside and local wall time inside, so a change to or from summer time neither skips nor
/// repeats a day: a time that doesn't exist that day (02:30 when the clock jumps to 03:00) fires at the jump, and a
/// time that happens twice (02:30 when the clock goes back) fires once, the first time.
/// </summary>
public static class EventSchedule
{
    /// <summary>The first time after <paramref name="afterUtc"/> (strictly) when it fires.</summary>
    public static DateTime Next(TimeOnly time, WeekDays days, DateTime afterUtc, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var localDay = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), zone).Date;
        for (var i = 0; i <= 8; i++)
        {
            var day = localDay.AddDays(i);
            if (!days.Includes(day.DayOfWeek)) continue;
            var utc = ToUtc(day + time.ToTimeSpan(), zone);
            if (utc > afterUtc) return utc;
        }
        return DateTime.MaxValue; // unreachable: some day of the next week always matches
    }

    /// <summary>On one <paramref name="date"/>: that moment if it is after <paramref name="afterUtc"/>, otherwise never.</summary>
    public static DateTime Next(TimeOnly time, DateOnly date, DateTime afterUtc, TimeZoneInfo? zone = null)
    {
        var utc = ToUtc(date.ToDateTime(time), zone ?? TimeZoneInfo.Local);
        return utc > afterUtc ? utc : DateTime.MaxValue;
    }

    /// <summary>On one <paramref name="date"/>: that moment if it is in (<paramref name="fromUtc"/>, <paramref name="toUtc"/>].</summary>
    public static DateTime? LastBetween(TimeOnly time, DateOnly date, DateTime fromUtc, DateTime toUtc, TimeZoneInfo? zone = null)
    {
        var utc = ToUtc(date.ToDateTime(time), zone ?? TimeZoneInfo.Local);
        return utc > fromUtc && utc <= toUtc ? utc : null;
    }

    /// <summary>The last time it should have fired in (<paramref name="fromUtc"/>, <paramref name="toUtc"/>], if any.</summary>
    public static DateTime? LastBetween(TimeOnly time, WeekDays days, DateTime fromUtc, DateTime toUtc, TimeZoneInfo? zone = null)
    {
        if (toUtc <= fromUtc) return null;
        DateTime? last = null;
        var next = Next(time, days, fromUtc, zone);
        // A long time closed only needs the last one (and not a loop over months).
        if ((toUtc - fromUtc).TotalDays > 9) next = Next(time, days, toUtc.AddDays(-8), zone);
        while (next <= toUtc)
        {
            last = next;
            next = Next(time, days, next, zone);
        }
        return last;
    }

    /// <summary>Local wall time → UTC; a time skipped by summer time moves to the moment the clock jumps.</summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var guard = 0;
        while (zone.IsInvalidTime(local) && guard++ < 180) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local))
        {
            // Twice that day: the first one (the larger offset, still summer time).
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
