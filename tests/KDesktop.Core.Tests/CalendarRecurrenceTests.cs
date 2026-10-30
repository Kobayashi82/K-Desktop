using KDesktop.Core.Calendar;

namespace KDesktop.Core.Tests;

public class CalendarRecurrenceTests
{
    private const string Madrid = "Europe/Madrid";

    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Series(DateTime start, string rule, string? zone = Madrid, TimeSpan? length = null, bool allDay = false) => new()
    {
        Id = "s", Uid = "s", SourceId = "local", CalendarId = "personal", Title = "Serie",
        Start = start, End = start + (length ?? (allDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1))),
        AllDay = allDay, TimeZone = allDay ? null : zone, Recurrence = rule,
    };

    private static List<DateTime> Walls(CalendarEvent e, DateTimeOffset from, DateTimeOffset to) =>
        Recurrence.Expand(e, from, to).Select(o => o.RecurrenceId ?? o.Event.Start).ToList();

    [Fact]
    public void ParsesAndWritesBackTheSameRule()
    {
        const string text = "FREQ=MONTHLY;INTERVAL=2;COUNT=10;BYDAY=2TU,-1FR;BYSETPOS=1;WKST=SU;BYHOUR=9";
        var rule = RecurrenceRule.Parse("RRULE:" + text)!;
        Assert.Equal(RecurrenceFrequency.Monthly, rule.Frequency);
        Assert.Equal(2, rule.Interval);
        Assert.Equal(10, rule.Count);
        Assert.Equal([new WeekdayNum(2, DayOfWeek.Tuesday), new WeekdayNum(-1, DayOfWeek.Friday)], rule.ByDay);
        Assert.Equal(DayOfWeek.Sunday, rule.WeekStart);
        Assert.Equal(["BYHOUR=9"], rule.Rest);
        Assert.Equal(RecurrenceRule.Parse(rule.ToString())!.ToString(), rule.ToString());
        Assert.Null(RecurrenceRule.Parse("FREQ=HOURLY"));
        Assert.Null(RecurrenceRule.Parse("nada"));
    }

    [Fact]
    public void WeeklyOnSomeDaysWithCount()
    {
        // Monday 5 Jan 2026, Mondays and Wednesdays, 5 times
        var e = Series(new DateTime(2026, 1, 5, 9, 0, 0), "FREQ=WEEKLY;BYDAY=MO,WE;COUNT=5");
        Assert.Equal(
            [new(2026, 1, 5, 9, 0, 0), new(2026, 1, 7, 9, 0, 0), new(2026, 1, 12, 9, 0, 0), new(2026, 1, 14, 9, 0, 0), new(2026, 1, 19, 9, 0, 0)],
            Walls(e, From, From.AddYears(1)));
    }

    [Fact]
    public void EveryOtherWeekStartsFromTheFirstWeek()
    {
        var e = Series(new DateTime(2026, 1, 7, 18, 0, 0), "FREQ=WEEKLY;INTERVAL=2;BYDAY=WE;COUNT=3");
        Assert.Equal([new(2026, 1, 7, 18, 0, 0), new(2026, 1, 21, 18, 0, 0), new(2026, 2, 4, 18, 0, 0)], Walls(e, From, From.AddYears(1)));
    }

    [Fact]
    public void MonthlyOnThe31stSkipsShortMonths()
    {
        var e = Series(new DateTime(2026, 1, 31, 10, 0, 0), "FREQ=MONTHLY;COUNT=4");
        Assert.Equal([new(2026, 1, 31, 10, 0, 0), new(2026, 3, 31, 10, 0, 0), new(2026, 5, 31, 10, 0, 0), new(2026, 7, 31, 10, 0, 0)],
            Walls(e, From, From.AddYears(2)));
    }

    [Fact]
    public void MonthlyLastDayAndSecondTuesday()
    {
        var last = Series(new DateTime(2026, 1, 31, 10, 0, 0), "FREQ=MONTHLY;BYMONTHDAY=-1;COUNT=3");
        Assert.Equal([new(2026, 1, 31, 10, 0, 0), new(2026, 2, 28, 10, 0, 0), new(2026, 3, 31, 10, 0, 0)], Walls(last, From, From.AddYears(1)));

        var tuesday = Series(new DateTime(2026, 1, 13, 19, 0, 0), "FREQ=MONTHLY;BYDAY=2TU;COUNT=3");
        Assert.Equal([new(2026, 1, 13, 19, 0, 0), new(2026, 2, 10, 19, 0, 0), new(2026, 3, 10, 19, 0, 0)], Walls(tuesday, From, From.AddYears(1)));

        var lastFriday = Series(new DateTime(2026, 1, 30, 8, 0, 0), "FREQ=MONTHLY;BYDAY=-1FR;COUNT=2");
        Assert.Equal([new(2026, 1, 30, 8, 0, 0), new(2026, 2, 27, 8, 0, 0)], Walls(lastFriday, From, From.AddYears(1)));
    }

    [Fact]
    public void LastWorkingDayWithSetPos()
    {
        var e = Series(new DateTime(2026, 1, 30, 17, 0, 0), "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=3");
        // January 30 is a Friday; February 27 a Friday; May 29 a Friday… March 31 is a Tuesday.
        Assert.Equal([new(2026, 1, 30, 17, 0, 0), new(2026, 2, 27, 17, 0, 0), new(2026, 3, 31, 17, 0, 0)], Walls(e, From, From.AddYears(1)));
    }

    [Fact]
    public void YearlyOn29FebruaryOnlyInLeapYears()
    {
        var e = Series(new DateTime(2024, 2, 29), "FREQ=YEARLY;COUNT=3", allDay: true);
        Assert.Equal([new(2024, 2, 29), new(2028, 2, 29), new(2032, 2, 29)],
            Walls(e, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void UntilIncludesThatDay()
    {
        var e = Series(new DateTime(2026, 3, 1, 9, 0, 0), "FREQ=DAILY;UNTIL=20260303T080000Z");
        Assert.Equal([new(2026, 3, 1, 9, 0, 0), new(2026, 3, 2, 9, 0, 0), new(2026, 3, 3, 9, 0, 0)], Walls(e, From, From.AddYears(1)));
        var floating = Series(new DateTime(2026, 3, 1, 9, 0, 0), "FREQ=DAILY;UNTIL=20260302T090000", zone: null);
        Assert.Equal(2, Walls(floating, From, From.AddYears(1)).Count);
    }

    [Fact]
    public void StaysAtTheSameHourAcrossSummerTime()
    {
        // 9:00 in Madrid every day around 29 March 2026 (summer time starts): 8:00 UTC before, 7:00 UTC after.
        var e = Series(new DateTime(2026, 3, 28, 9, 0, 0), "FREQ=DAILY;COUNT=3");
        var list = Recurrence.Expand(e, From, From.AddYears(1)).ToList();
        Assert.Equal([8, 7, 7], list.Select(o => o.Start.UtcDateTime.Hour));
        Assert.All(list, o => Assert.Equal(TimeSpan.FromHours(1), o.End - o.Start));
    }

    [Fact]
    public void TimesThatDoNotExistOrHappenTwice()
    {
        var madrid = CalendarTime.Zone(Madrid);
        // 2:30 on 29 March 2026 does not exist in Madrid: it is read as 3:30 (01:30 UTC).
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), CalendarTime.ToInstant(new DateTime(2026, 3, 29, 2, 30, 0), madrid).UtcDateTime);
        // 2:30 on 25 October 2026 happens twice: the first (summer, 00:30 UTC).
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0), CalendarTime.ToInstant(new DateTime(2026, 10, 25, 2, 30, 0), madrid).UtcDateTime);
    }

    [Fact]
    public void AllDayKeepsItsDate()
    {
        var e = new CalendarEvent { Id = "a", Uid = "a", Title = "Cumple", Start = new DateTime(2026, 5, 10), End = new DateTime(2026, 5, 11), AllDay = true, TimeZone = "Asia/Tokyo" };
        var o = Assert.Single(Recurrence.Expand(e, From, From.AddYears(1)));
        Assert.Equal(new DateOnly(2026, 5, 10), o.Date);
        Assert.Equal(TimeSpan.FromDays(1), o.End - o.Start);
    }

    [Fact]
    public void ExceptionsAndOverrides()
    {
        var e = Series(new DateTime(2026, 2, 2, 9, 0, 0), "FREQ=DAILY;COUNT=5") with
        {
            Exceptions = [new DateTime(2026, 2, 3, 9, 0, 0)],
            Overrides =
            [
                new CalendarEvent { Id = "o1", Uid = "s", Title = "Movida", Start = new DateTime(2026, 2, 4, 12, 0, 0), End = new DateTime(2026, 2, 4, 13, 0, 0), TimeZone = Madrid, RecurrenceId = new DateTime(2026, 2, 4, 9, 0, 0) },
                new CalendarEvent { Id = "o2", Uid = "s", Title = "Borrada", Start = new DateTime(2026, 2, 5, 9, 0, 0), End = new DateTime(2026, 2, 5, 10, 0, 0), TimeZone = Madrid, RecurrenceId = new DateTime(2026, 2, 5, 9, 0, 0), Cancelled = true },
            ],
        };
        var list = Recurrence.Expand(e, From, From.AddYears(1)).ToList();
        Assert.Equal(["Serie", "Movida", "Serie"], list.Select(o => o.Event.Title));
        Assert.Equal(12, list[1].Start.ToOffset(TimeSpan.FromHours(1)).Hour);
        Assert.All(list, o => Assert.Same(e, o.Series));
    }

    [Fact]
    public void OnlyWhatTouchesTheRange()
    {
        var e = Series(new DateTime(2020, 1, 1, 9, 0, 0), "FREQ=DAILY");
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2));
        var list = Recurrence.Expand(e, from, from.AddDays(7)).ToList();
        Assert.Equal(7, list.Count);
        Assert.Equal(new DateTime(2026, 6, 1, 9, 0, 0), list[0].RecurrenceId);
    }

    [Fact]
    public void ThisAndFollowingCutsTheSeries()
    {
        var e = Series(new DateTime(2026, 1, 5, 9, 0, 0), "FREQ=WEEKLY;BYDAY=MO;COUNT=10");
        var at = new DateTime(2026, 1, 26, 9, 0, 0); // the 4th
        var (before, after) = CalendarEdits.Split(e, at, e with { Start = at.AddHours(1), End = at.AddHours(2), Title = "Nueva hora" });
        Assert.NotNull(before);
        Assert.Equal(3, Walls(before!, From, From.AddYears(1)).Count);
        var next = Walls(after, From, From.AddYears(1));
        Assert.Equal(7, next.Count);
        Assert.Equal(new DateTime(2026, 1, 26, 10, 0, 0), next[0]);
        Assert.NotEqual(e.Uid, after.Uid);
    }

    [Fact]
    public void ThisAndFollowingFromTheFirstChangesTheWholeSeries()
    {
        var e = Series(new DateTime(2026, 1, 5, 9, 0, 0), "FREQ=DAILY");
        var (before, after) = CalendarEdits.Split(e, e.Start, e with { Title = "Otra" });
        Assert.Null(before);
        Assert.Equal(e.Uid, after.Uid);
    }

    [Fact]
    public void ThisAndFollowingWithoutCountUsesUntilInUtc()
    {
        var e = Series(new DateTime(2026, 1, 5, 9, 0, 0), "FREQ=DAILY");
        var (before, _) = CalendarEdits.Split(e, new DateTime(2026, 1, 8, 9, 0, 0), e);
        var rule = RecurrenceRule.Parse(before!.Recurrence)!;
        Assert.True(rule.UntilUtc);
        Assert.Equal(3, Walls(before, From, From.AddYears(1)).Count);
    }

    [Fact]
    public void ChangeAllMovesTheExceptionsToo()
    {
        var e = Series(new DateTime(2026, 1, 5, 9, 0, 0), "FREQ=DAILY;COUNT=4") with { Exceptions = [new DateTime(2026, 1, 6, 9, 0, 0)] };
        var occurrence = new DateTime(2026, 1, 7, 9, 0, 0);
        var all = CalendarEdits.ChangeAll(e, occurrence, e with { Start = occurrence.AddHours(1), End = occurrence.AddHours(2) });
        Assert.Equal(new DateTime(2026, 1, 5, 10, 0, 0), all.Start);
        Assert.Equal([new DateTime(2026, 1, 5, 10, 0, 0), new DateTime(2026, 1, 7, 10, 0, 0), new DateTime(2026, 1, 8, 10, 0, 0)], Walls(all, From, From.AddYears(1)));
    }

    [Fact]
    public void OneAppearanceOnly()
    {
        var e = Series(new DateTime(2026, 1, 5, 9, 0, 0), "FREQ=DAILY;COUNT=3");
        var rid = new DateTime(2026, 1, 6, 9, 0, 0);
        var changed = CalendarEdits.WithOverride(e, rid, e with { Title = "Solo hoy", Start = rid.AddHours(2), End = rid.AddHours(3) });
        Assert.Equal(["Serie", "Solo hoy", "Serie"], Recurrence.Expand(changed, From, From.AddYears(1)).Select(o => o.Event.Title));
        var without = CalendarEdits.WithoutOccurrence(changed, rid);
        Assert.Equal(2, Recurrence.Expand(without, From, From.AddYears(1)).Count());
        Assert.Empty(without.Overrides);
    }
}
