using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 13.2: the SDK's Variety (weights, no repeats, cooldowns, seed).</summary>
public class VarietyTests
{
    private double _now;

    private Variety<string> New(int seed = 7) => new(seed, () => _now);

    [Fact]
    public void TheSameSeedGivesTheSameSequence()
    {
        var a = New(42).Add("a").Add("b").Add("c").Add("d");
        var b = New(42).Add("a").Add("b").Add("c").Add("d");
        var first = Enumerable.Range(0, 50).Select(_ => a.Pick()).ToList();
        var second = Enumerable.Range(0, 50).Select(_ => b.Pick()).ToList();
        Assert.Equal(first, second);
    }

    [Fact]
    public void ItNeverRepeatsTheLastWhileThereIsAnother()
    {
        var variety = New().Add("a", weight: 100).Add("b", weight: 1);
        var picks = Enumerable.Range(0, 200).Select(_ => variety.Pick()).ToList();
        for (var i = 1; i < picks.Count; i++) Assert.NotEqual(picks[i - 1], picks[i]);

        // With repeats allowed, the heavy one wins nearly always.
        var free = New().Add("a", weight: 100).Add("b", weight: 1);
        free.NoRepeat = false;
        Assert.True(Enumerable.Range(0, 200).Count(_ => free.Pick() == "a") > 180);
    }

    [Fact]
    public void ASingleVariantComesUpEveryTime()
    {
        var variety = New().Add("only");
        Assert.All(Enumerable.Range(0, 5), _ => Assert.Equal("only", variety.Pick()));
        Assert.True(variety.TryGetLast(out var last));
        Assert.Equal("only", last);
    }

    [Fact]
    public void WeightsSetHowOftenEachComesUp()
    {
        var variety = New(3).Add("a", weight: 3).Add("b", weight: 1).Add("never", weight: 0);
        variety.NoRepeat = false;
        var picks = Enumerable.Range(0, 4000).Select(_ => variety.Pick()).ToList();
        Assert.DoesNotContain("never", picks);
        Assert.InRange(picks.Count(p => p == "a") / 4000.0, 0.70, 0.80);
    }

    [Fact]
    public void ARestingVariantWaitsForItsCooldown()
    {
        var variety = New().Add("rare", weight: 1000, cooldown: 60).Add("a").Add("b");
        Assert.Equal("rare", variety.Pick());
        Assert.Equal(60, variety.CooldownLeft("rare"));
        for (var i = 0; i < 100; i++)
        {
            _now += 0.5;
            Assert.NotEqual("rare", variety.Pick());
        }

        _now = 61;
        Assert.Equal(0, variety.CooldownLeft("rare"));
        Assert.Equal("rare", variety.Pick());
    }

    [Fact]
    public void WhenAllRestPickGivesTheSoonestAndTryPickNothing()
    {
        var variety = New().Add("a", cooldown: 30).Add("b", cooldown: 10);
        var first = variety.Pick();
        _now = 1;
        var second = variety.Pick();
        Assert.NotEqual(first, second);

        _now = 2;
        Assert.False(variety.TryPick(out _));
        // «b» is ready at 10 or 11, «a» at 30 or 31: «b» comes out.
        Assert.Equal("b", variety.Pick());
    }

    [Fact]
    public void OnlyTheAllowedOnesComeOut()
    {
        var variety = New().Add("window").Add("taskbar").Add("anywhere");
        var picks = Enumerable.Range(0, 100).Select(_ => variety.Pick(v => v != "taskbar")).ToList();
        Assert.DoesNotContain("taskbar", picks);
        Assert.Throws<InvalidOperationException>(() => variety.Pick(_ => false));
        Assert.False(variety.TryPick(out _, _ => false));
    }

    [Fact]
    public void UseAndResetMarkAndForget()
    {
        var variety = New().Add("a", cooldown: 20).Add("b");
        variety.Use("a");
        Assert.True(variety.TryGetLast(out var last));
        Assert.Equal("a", last);
        Assert.Equal("b", variety.Pick());
        Assert.True(variety.CooldownLeft("a") > 0);

        variety.Reset();
        Assert.False(variety.TryGetLast(out _));
        Assert.Equal(0, variety.CooldownLeft("a"));
        variety.Use("missing");
        Assert.False(variety.TryGetLast(out _));
    }

    [Fact]
    public void BadWeightsAndCooldownsAreRefused()
    {
        var variety = New();
        Assert.Throws<ArgumentOutOfRangeException>(() => variety.Add("x", weight: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => variety.Add("x", cooldown: double.NaN));
        Assert.Throws<InvalidOperationException>(() => variety.Pick());
        Assert.Equal(0, variety.Count);
    }
}
