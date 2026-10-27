using System.Globalization;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

public class EventScheduleTests
{
    /// <summary>Madrid: summer time starts on the last Sunday of March (02:00 → 03:00) and ends on the last of October (03:00 → 02:00).</summary>
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Madrid);

    [Fact]
    public void LaterTodayOrTomorrow()
    {
        // Thursday 15 Oct 2026, 08:00 in Madrid (06:00 UTC, summer time)
        var next = EventSchedule.Next(new TimeOnly(9, 0), WeekDays.None, Utc(2026, 10, 15, 6, 0), Madrid);
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0), Local(next));
        next = EventSchedule.Next(new TimeOnly(9, 0), WeekDays.None, next, Madrid);
        Assert.Equal(new DateTime(2026, 10, 16, 9, 0, 0), Local(next));
    }

    [Fact]
    public void OnlyTheChosenDays()
    {
        // Friday 16 Oct 2026 at 10:00 → next workday at 9:00 is Monday 19
        var next = EventSchedule.Next(new TimeOnly(9, 0), WeekDays.Workdays, Utc(2026, 10, 16, 8, 0), Madrid);
        Assert.Equal(new DateTime(2026, 10, 19, 9, 0, 0), Local(next));
        Assert.Equal(DayOfWeek.Monday, Local(next).DayOfWeek);
    }

    [Fact]
    public void OnADateOnlyThatDay()
    {
        var date = new DateOnly(2026, 10, 30);
        var next = EventSchedule.Next(new TimeOnly(19, 0), date, Utc(2026, 10, 15, 6, 0), Madrid);
        Assert.Equal(new DateTime(2026, 10, 30, 19, 0, 0), Local(next));
        Assert.Equal(DateTime.MaxValue, EventSchedule.Next(new TimeOnly(19, 0), date, next, Madrid));
        Assert.Equal(next, EventSchedule.LastBetween(new TimeOnly(19, 0), date, next.AddHours(-1), next.AddHours(1), Madrid));
        Assert.Null(EventSchedule.LastBetween(new TimeOnly(19, 0), date, next, next.AddDays(3), Madrid));
    }

    [Fact]
    public void DateIsSavedInvariant()
    {
        var trigger = new TimeTrigger { DateValue = new DateTime(2026, 10, 30) };
        Assert.Equal("2026-10-30", trigger.Date);
        Assert.True(trigger.HasDate);
        trigger.DateValue = null;
        Assert.Equal(string.Empty, trigger.Date);
        Assert.False(trigger.HasDate);
    }

    [Fact]
    public void MidnightIsTheNextDay()
    {
        var next = EventSchedule.Next(new TimeOnly(0, 0), WeekDays.None, Utc(2026, 10, 15, 21, 30), Madrid); // 23:30 local
        Assert.Equal(new DateTime(2026, 10, 16, 0, 0, 0), Local(next));
    }

    [Fact]
    public void SummerTimeKeepsTheWallClock()
    {
        // 09:00 on Saturday 24 Oct (summer, UTC+2) and on Sunday 25 Oct (winter, UTC+1)
        var sat = EventSchedule.Next(new TimeOnly(9, 0), WeekDays.None, Utc(2026, 10, 24, 5, 0), Madrid);
        var sun = EventSchedule.Next(new TimeOnly(9, 0), WeekDays.None, sat, Madrid);
        Assert.Equal(Utc(2026, 10, 24, 7, 0), sat);
        Assert.Equal(Utc(2026, 10, 25, 8, 0), sun);
        Assert.Equal(TimeSpan.FromHours(25), sun - sat);
    }

    [Fact]
    public void TimeThatDoesNotExistFiresAtTheJump()
    {
        // 29 Mar 2026: 02:00 → 03:00. «02:30» fires at 03:00 local = 01:00 UTC.
        var next = EventSchedule.Next(new TimeOnly(2, 30), WeekDays.None, Utc(2026, 3, 28, 23, 0), Madrid);
        Assert.Equal(Utc(2026, 3, 29, 1, 0), next);
    }

    [Fact]
    public void TimeThatHappensTwiceFiresOnce()
    {
        // 25 Oct 2026: 03:00 → 02:00. «02:30» happens twice; it fires the first time (00:30 UTC) and next on the 26th.
        var first = EventSchedule.Next(new TimeOnly(2, 30), WeekDays.None, Utc(2026, 10, 24, 22, 0), Madrid);
        Assert.Equal(Utc(2026, 10, 25, 0, 30), first);
        var second = EventSchedule.Next(new TimeOnly(2, 30), WeekDays.None, first, Madrid);
        Assert.Equal(Utc(2026, 10, 26, 1, 30), second);
    }

    [Fact]
    public void MissedTimeIsTheLastOneInTheGap()
    {
        // Closed from Thursday 15 at 08:00 to Saturday 17 at 12:00: the last 9:00 missed is Saturday's
        var missed = EventSchedule.LastBetween(new TimeOnly(9, 0), WeekDays.None, Utc(2026, 10, 15, 6, 0), Utc(2026, 10, 17, 10, 0), Madrid);
        Assert.Equal(new DateTime(2026, 10, 17, 9, 0, 0), Local(missed!.Value));
        // Only workdays: Friday's
        missed = EventSchedule.LastBetween(new TimeOnly(9, 0), WeekDays.Workdays, Utc(2026, 10, 15, 6, 0), Utc(2026, 10, 17, 10, 0), Madrid);
        Assert.Equal(new DateTime(2026, 10, 16, 9, 0, 0), Local(missed!.Value));
    }

    [Fact]
    public void NothingMissedWhenTheGapHasNoTime()
    {
        Assert.Null(EventSchedule.LastBetween(new TimeOnly(9, 0), WeekDays.None, Utc(2026, 10, 15, 8, 0), Utc(2026, 10, 15, 15, 0), Madrid));
        // A time already done (fired at 9:00 exactly) is not missed again
        Assert.Null(EventSchedule.LastBetween(new TimeOnly(9, 0), WeekDays.None, Utc(2026, 10, 15, 7, 0), Utc(2026, 10, 15, 7, 0), Madrid));
        Assert.Null(EventSchedule.LastBetween(new TimeOnly(9, 0), WeekDays.None, Utc(2026, 10, 15, 7, 0), Utc(2026, 10, 15, 7, 30), Madrid));
    }

    [Fact]
    public void LongGapOnlyNeedsTheLastOne()
    {
        var missed = EventSchedule.LastBetween(new TimeOnly(9, 0), WeekDays.None, Utc(2025, 1, 1, 0, 0), Utc(2026, 10, 15, 10, 0), Madrid);
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0), Local(missed!.Value));
    }

    [Theory]
    [InlineData("22:00", "06:00", "23:30", true)]
    [InlineData("22:00", "06:00", "05:59", true)]
    [InlineData("22:00", "06:00", "06:00", false)]
    [InlineData("22:00", "06:00", "12:00", false)]
    [InlineData("09:00", "18:00", "09:00", true)]
    [InlineData("09:00", "18:00", "18:00", false)]
    [InlineData("09:00", "", "23:59", true)]
    [InlineData("", "08:00", "07:00", true)]
    [InlineData("", "08:00", "09:00", false)]
    public void TimeRangeCondition(string from, string to, string now, bool expected)
    {
        var c = new EventConditions { TimeFrom = from, TimeTo = to };
        var at = new DateTime(2026, 10, 16) + TimeOnly.Parse(now, CultureInfo.InvariantCulture).ToTimeSpan();
        Assert.Equal(expected, c.Allows(at, _ => false, out _));
    }

    [Fact]
    public void DayAndProcessConditions()
    {
        var friday = new DateTime(2026, 10, 16, 12, 0, 0);
        Assert.True(new EventConditions { Days = WeekDays.Workdays }.Allows(friday, _ => false, out _));
        Assert.False(new EventConditions { Days = WeekDays.Weekend }.Allows(friday, _ => false, out var reason));
        Assert.NotEmpty(reason);
        Assert.True(new EventConditions { ProcessRunning = "Steam.exe" }.Allows(friday, n => n == "steam", out _));
        Assert.False(new EventConditions { ProcessRunning = "steam" }.Allows(friday, _ => false, out _));
        Assert.False(new EventConditions { ProcessNotRunning = "obs64" }.Allows(friday, n => n == "obs64", out _));
    }
}
