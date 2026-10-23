using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>SDK 1.7: the beat of the music that plays (the tracker from Kobii and what each assistant sees).</summary>
public class MusicTests
{
    private static double Range(Random random, double from, double to) => from + random.NextDouble() * (to - from);

    /// <summary>A made-up song: a kick on every beat over a background with some noise.</summary>
    private static double Song(double t, double bpm, Random random, double start = 0)
    {
        var period = 60 / bpm;
        var since = (t - start) % period;
        var kick = t >= start ? 0.8 * Math.Exp(-since * 18) : 0;
        var offbeat = t >= start ? 0.25 * Math.Exp(-((since + period / 2) % period) * 25) : 0;
        return Math.Clamp(0.12 + kick + offbeat + Range(random, -0.04, 0.04), 0, 1);
    }

    private static void Feed(BeatTracker tracker, double from, double to, Func<double, double> level, double rate = 97)
    {
        for (var t = from; t < to; t += 1 / rate) tracker.Add(t, level(t));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(95)]
    [InlineData(140)]
    public void ItFindsTheTempoAndTheBeatOfASong(double bpm)
    {
        var tracker = new BeatTracker();
        var random = new Random(7);
        Feed(tracker, 0, 10, t => Song(t, bpm, random, start: 0.3));
        Assert.True(tracker.Playing, "It should hear music.");
        Assert.InRange(tracker.Bpm, bpm * 0.97, bpm * 1.03);

        // On the kicks, the phase is close to zero.
        var period = 60 / bpm;
        var kick = 0.3 + Math.Ceiling((10 - 0.3) / period) * period;
        Feed(tracker, 10, kick, t => Song(t, bpm, random, start: 0.3));
        var phase = tracker.Beat(kick) - Math.Round(tracker.Beat(kick));
        Assert.True(Math.Abs(phase) < 0.15, $"Phase on the kick: {phase:0.00}.");
    }

    [Fact]
    public void SilenceIsNotMusicAndMusicStopsWhenItStops()
    {
        var tracker = new BeatTracker();
        Feed(tracker, 0, 6, _ => 0);
        Assert.False(tracker.Playing);

        var random = new Random(3);
        Feed(tracker, 6, 14, t => Song(t, 110, random, start: 6));
        Assert.True(tracker.Playing);
        Feed(tracker, 14, 17, _ => 0);
        Assert.False(tracker.Playing, "When the music stops, it stops dancing.");
    }

    [Fact]
    public void SomeoneTalkingIsNotMusic()
    {
        // Syllables of irregular length and gaps, like a voice.
        var tracker = new BeatTracker();
        var random = new Random(11);
        var next = 0.0;
        var until = 0.0;
        var loud = 0.0;
        var playing = 0.0;
        for (var t = 0.0; t < 20; t += 0.01)
        {
            if (t >= next)
            {
                until = t + Range(random, 0.08, 0.3);
                next = until + Range(random, 0.03, 0.5) + (random.NextDouble() < 0.15 ? Range(random, 0.4, 1.2) : 0);
                loud = Range(random, 0.2, 0.7);
            }

            tracker.Add(t, t < until ? loud * Range(random, 0.7, 1) : Range(random, 0, 0.02));
            playing += tracker.Playing ? 0.01 : 0;
        }

        Assert.True(playing < 3, $"A voice should not look like music ({playing:0.0} s).");
    }

    [Fact]
    public void TheBeatGoesOnBetweenReadings()
    {
        var beat = new MusicBeat(true, 120, 10.25, 5);
        Assert.Equal(12.25, beat.BeatAt(6), 6);
        Assert.Equal(3, default(MusicBeat).BeatAt(100) + 3);
    }

    [Fact]
    public void OnlyWhoListensHearsAndTheMeterRunsWhileSomeoneDoes()
    {
        using var world = new HostWorld(() => 1);
        var dancer = new WorldView(world);
        var other = new WorldView(world);
        Assert.False(dancer.Music.Playing);

        dancer.ListenToMusic(true);
        other.ListenToMusic(true);
        other.ListenToMusic(false);
        Assert.Equal(1, dancer.Music.At);
        Assert.Equal(0, other.Music.At);

        // Unloading an assistant stops its listening, and with nobody listening nothing is measured.
        dancer.Forget();
        Assert.Equal(0, dancer.Music.At);
        Assert.Equal(0, world.Music.At);
    }
}
