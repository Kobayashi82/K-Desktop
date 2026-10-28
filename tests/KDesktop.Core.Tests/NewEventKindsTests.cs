using KDesktop.Core.Engine;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>5.0.341: battery level, sunrise and sunset, screens, what happens to a file.</summary>
public class NewEventKindsTests
{
    private const double MadridLat = 40.4168, MadridLon = -3.7038;

    [Fact]
    public void SunriseAndSunsetInMadrid()
    {
        // 21 June 2026: about 04:44 and 19:48 UTC; 21 December: about 07:33 and 16:55 UTC (within a few minutes).
        var (rise, set) = SunTimes.For(new DateOnly(2026, 6, 21), MadridLat, MadridLon);
        Near(new DateTime(2026, 6, 21, 4, 44, 0, DateTimeKind.Utc), rise!.Value);
        Near(new DateTime(2026, 6, 21, 19, 48, 0, DateTimeKind.Utc), set!.Value);
        (rise, set) = SunTimes.For(new DateOnly(2026, 12, 21), MadridLat, MadridLon);
        Near(new DateTime(2026, 12, 21, 7, 33, 0, DateTimeKind.Utc), rise!.Value);
        Near(new DateTime(2026, 12, 21, 16, 55, 0, DateTimeKind.Utc), set!.Value);
    }

    private static void Near(DateTime expected, DateTime actual) =>
        Assert.True(Math.Abs((actual - expected).TotalMinutes) < 5, $"{actual:u} no está cerca de {expected:u}");

    [Fact]
    public void TheNextSunEventWithItsOffset()
    {
        var noon = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var next = SunTimes.Next(SunEvent.Sunset, -30, MadridLat, MadridLon, noon);
        Near(new DateTime(2026, 6, 21, 19, 18, 0, DateTimeKind.Utc), next);
        // Past today's sunrise: tomorrow's.
        Assert.Equal(22, SunTimes.Next(SunEvent.Sunrise, 0, MadridLat, MadridLon, noon).Day);
        Assert.True(SunTimes.IsDay(noon, MadridLat, MadridLon));
        Assert.False(SunTimes.IsDay(new DateTime(2026, 6, 21, 23, 0, 0, DateTimeKind.Utc), MadridLat, MadridLon));
        // Polar day and night.
        Assert.Equal((null, null), SunTimes.For(new DateOnly(2026, 6, 21), 78, 15));
        Assert.True(SunTimes.IsDay(new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc), 78, 15));
        Assert.False(SunTimes.IsDay(new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc), 78, 15));
    }

    [Fact]
    public void AnOldFileEventIsStillAboutNewFiles()
    {
        var old = Assert.IsType<FileTrigger>(Json.Deserialize<EventTrigger>("""{"type":"file","folder":"C:\\x","filter":"*.png"}"""));
        Assert.Equal(FileChange.Created, old.Change);
        var changed = new FileTrigger { Folder = @"C:\out", Change = FileChange.Deleted };
        Assert.Equal(FileChange.Deleted, Assert.IsType<FileTrigger>(Json.Deserialize<EventTrigger>(Json.Serialize<EventTrigger>(changed))).Change);
    }

    [Fact]
    public void TheNewTriggersSurviveSavingAndSayWhatTheyDo()
    {
        EventTrigger[] all =
        [
            new BatteryTrigger { Comparison = LoadComparison.Below, Percent = 15 },
            new SunTrigger { Event = SunEvent.Sunrise, OffsetMinutes = -20, Place = "Málaga" },
            new DisplayTrigger { Event = DisplayEvent.Disconnected, Filter = "LG" },
        ];
        foreach (var trigger in all)
        {
            var back = Json.Deserialize<EventTrigger>(Json.Serialize(trigger))!;
            Assert.Equal(Json.Serialize(trigger), Json.Serialize(back));
            Assert.False(string.IsNullOrWhiteSpace(back.Summary));
            Assert.DoesNotContain("event.", back.Summary, StringComparison.Ordinal);
            Assert.Same(back.GetType(), EventCatalog.Get(back.Kind).Create().GetType());
        }
        Assert.True(new BatteryTrigger { Percent = 20 }.Holds(19));
        Assert.False(new BatteryTrigger { Percent = 20 }.Holds(20));
        Assert.True(new DisplayTrigger { Filter = "lg" }.Matches("LG ULTRAGEAR"));
        Assert.True(new DisplayTrigger().Matches("Cualquiera"));
        Assert.True(new DisplayTrigger().HasEnd);
        Assert.False(new DisplayTrigger { Event = DisplayEvent.Disconnected }.HasEnd);
    }

    [Fact]
    public void TheNewConditionsSayWhatTheyCheck()
    {
        var battery = new ConditionAction { Condition = ConditionKind.BatteryLevel };
        Assert.Equal(20, battery.Percent);
        Assert.Contains("20", battery.Describe(), StringComparison.Ordinal);
        var screens = new ConditionAction { Condition = ConditionKind.Screens };
        Assert.Equal(2, screens.Percent);
        Assert.Contains("2", screens.Describe(), StringComparison.Ordinal);
        var day = new ConditionAction { Condition = ConditionKind.Daylight, Negate = true };
        Assert.DoesNotContain("if.", day.Describe(), StringComparison.Ordinal);
    }
}
