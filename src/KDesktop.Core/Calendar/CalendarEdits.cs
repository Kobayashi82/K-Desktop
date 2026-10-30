using System.Globalization;

namespace KDesktop.Core.Calendar;

/// <summary>Which appearances a change of a series touches.</summary>
public enum EditScope
{
    /// <summary>«Solo esta».</summary>
    This,
    /// <summary>«Esta y las siguientes».</summary>
    ThisAndFollowing,
    /// <summary>«Todas».</summary>
    All,
}

/// <summary>
/// The changes of a series, worked out without any source: one appearance (an override), this one and the following
/// (the series is cut in two) or all of them. Pure functions, so they are tested on their own.
/// </summary>
public static class CalendarEdits
{
    public static string OverrideId(CalendarEvent series, DateTime recurrenceId) =>
        series.Id + "_" + recurrenceId.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    /// <summary>The series with <paramref name="changed"/> in place of the appearance <paramref name="recurrenceId"/>.</summary>
    public static CalendarEvent WithOverride(CalendarEvent series, DateTime recurrenceId, CalendarEvent changed)
    {
        var o = changed with
        {
            Id = OverrideId(series, recurrenceId),
            Uid = series.Uid,
            SourceId = series.SourceId,
            CalendarId = series.CalendarId,
            RecurrenceId = recurrenceId,
            Recurrence = null,
            Exceptions = [],
            Overrides = [],
            Cancelled = false,
            Etag = null,
            Updated = DateTimeOffset.UtcNow,
        };
        return series with { Overrides = [.. series.Overrides.Where(x => x.RecurrenceId != recurrenceId), o] };
    }

    /// <summary>The series without the appearance <paramref name="recurrenceId"/> (an EXDATE; its override, if any, goes too).</summary>
    public static CalendarEvent WithoutOccurrence(CalendarEvent series, DateTime recurrenceId) => series with
    {
        Exceptions = [.. series.Exceptions.Where(x => x != recurrenceId), recurrenceId],
        Overrides = [.. series.Overrides.Where(x => x.RecurrenceId != recurrenceId)],
    };

    /// <summary>
    /// «Esta y las siguientes»: the series up to before <paramref name="recurrenceId"/> (null when that was the first:
    /// the whole series changes) and a new series from it on, with <paramref name="changed"/>'s data (its own rule if it
    /// has one; otherwise the old one, with the times left when it counted them). The deleted and changed appearances
    /// go with the part they belong to, moved by as much as the start moved.
    /// </summary>
    public static (CalendarEvent? Before, CalendarEvent After) Split(CalendarEvent series, DateTime recurrenceId, CalendarEvent changed)
    {
        var rule = RecurrenceRule.Parse(series.Recurrence);
        var before = Recurrence.CountBefore(series, recurrenceId);
        var shift = changed.Start - recurrenceId;

        RecurrenceRule? afterRule = RecurrenceRule.Parse(changed.Recurrence) ?? rule;
        if (afterRule is not null && rule is not null && changed.Recurrence == series.Recurrence && rule.Count is { } count)
            afterRule = afterRule with { Count = Math.Max(1, count - before) };

        var uid = CalendarEvent.NewUid();
        var after = changed with
        {
            Id = before == 0 ? series.Id : uid,
            Uid = before == 0 ? series.Uid : uid,
            SourceId = series.SourceId,
            CalendarId = changed.CalendarId.Length > 0 ? changed.CalendarId : series.CalendarId,
            RecurrenceId = null,
            Recurrence = afterRule?.ToString(),
            Exceptions = [.. series.Exceptions.Where(x => x >= recurrenceId).Select(x => x + shift)],
            Overrides = [.. series.Overrides.Where(o => o.RecurrenceId >= recurrenceId && o.RecurrenceId != recurrenceId)
                .Select(o => o with { RecurrenceId = o.RecurrenceId + shift, Uid = before == 0 ? series.Uid : uid })],
            Etag = before == 0 ? series.Etag : null,
            Updated = DateTimeOffset.UtcNow,
        };
        if (before == 0 || rule is null) return (null, after);

        RecurrenceRule cut;
        if (rule.Count is not null) cut = rule with { Count = before, Until = null };
        else if (series.AllDay) cut = rule with { Until = recurrenceId.Date.AddDays(-1), UntilIsDate = true, UntilUtc = false };
        else if (series.TimeZone is null) cut = rule with { Until = recurrenceId.AddSeconds(-1), UntilUtc = false, UntilIsDate = false };
        else
        {
            var utc = CalendarTime.ToInstant(recurrenceId, CalendarTime.Zone(series.TimeZone)).UtcDateTime.AddSeconds(-1);
            cut = rule with { Until = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), UntilUtc = true, UntilIsDate = false };
        }
        var first = series with
        {
            Recurrence = cut.ToString(),
            Exceptions = [.. series.Exceptions.Where(x => x < recurrenceId)],
            Overrides = [.. series.Overrides.Where(o => o.RecurrenceId < recurrenceId)],
            Updated = DateTimeOffset.UtcNow,
        };
        return (first, after);
    }

    /// <summary>
    /// «Todas»: the series takes <paramref name="changed"/>'s data; the start moves as much as the appearance
    /// <paramref name="recurrenceId"/> moved (from 9:00 to 10:00: every one an hour later), and so do the deleted and
    /// changed appearances, so they still point at theirs.
    /// </summary>
    public static CalendarEvent ChangeAll(CalendarEvent series, DateTime recurrenceId, CalendarEvent changed)
    {
        var shift = changed.Start - recurrenceId;
        var start = series.Start + shift;
        return changed with
        {
            Id = series.Id,
            Uid = series.Uid,
            SourceId = series.SourceId,
            CalendarId = changed.CalendarId.Length > 0 ? changed.CalendarId : series.CalendarId,
            Start = start,
            End = start + changed.Duration,
            RecurrenceId = null,
            Recurrence = changed.Recurrence ?? series.Recurrence,
            Exceptions = [.. series.Exceptions.Select(x => x + shift)],
            Overrides = [.. series.Overrides.Select(o => o with { RecurrenceId = o.RecurrenceId + shift })],
            Etag = series.Etag,
            Updated = DateTimeOffset.UtcNow,
        };
    }
}
