using System.Diagnostics;
using KDesktop.Core;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// Where the time of each engine tick goes, written to the log every 30 s (only with <c>KDESKTOP_RENDER_STATS=1</c>): the
/// world, the assistants' own ticks, and for each frame recording the WPF drawing, replaying it with Direct2D,
/// <c>EndDraw</c> (where Direct2D turns the figures into triangles and sends them), placing and committing.
/// </summary>
internal static class RenderStats
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("KDESKTOP_RENDER_STATS") == "1";

    private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;
    private static readonly long[] Totals = new long[(int)Part.Count];
    private static long _ticks;
    private static long _frames;
    private static long _geometries;
    private static long _layers;
    private static long _primitives;
    private static long _wakes;
    private static long _allocated = GC.GetAllocatedBytesForCurrentThread();
    private static int _collections = GC.CollectionCount(0);
    private static long _since = Stopwatch.GetTimestamp();

    public enum Part { Awake, Tick, World, Assistants, Record, Replay, EndDraw, Present, Commit, Count }

    public static long Now() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void Add(Part part, long start)
    {
        if (Enabled) Totals[(int)part] += Stopwatch.GetTimestamp() - start;
    }

    /// <summary>The engine loop goes back to sleep: how long it was awake (messages, orders and the tick).</summary>
    public static void Wake(long start)
    {
        if (!Enabled) return;
        Add(Part.Awake, start);
        _wakes++;
    }

    public static void Frame() => _frames++;

    public static void Geometry() => _geometries++;

    public static void Layer() => _layers++;

    public static void Primitive() => _primitives++;

    /// <summary>End of a tick: every 30 s, one line in the log.</summary>
    public static void Tick(long start)
    {
        if (!Enabled) return;
        Add(Part.Tick, start);
        _ticks++;
        var now = Stopwatch.GetTimestamp();
        var seconds = (now - _since) / (double)Stopwatch.Frequency;
        if (seconds < 30) return;
        double PerFrame(Part part) => Totals[(int)part] * TickToMs / Math.Max(1, _frames);
        double PerTick(Part part) => Totals[(int)part] * TickToMs / Math.Max(1, _ticks);
        Log.Info(FormattableString.Invariant(
            $"Render: {_wakes / seconds:0.0} despertares/s ({Totals[(int)Part.Awake] * TickToMs / Math.Max(1, _wakes):0.000} ms despierto), {_ticks / seconds:0.0} ciclos/s y {_frames / seconds:0.0} fotogramas/s; por ciclo {PerTick(Part.Tick):0.000} ms (mundo {PerTick(Part.World):0.000}, asistentes {PerTick(Part.Assistants):0.000}, confirmar {PerTick(Part.Commit):0.000}); por fotograma grabar {PerFrame(Part.Record):0.000}, reproducir {PerFrame(Part.Replay):0.000}, EndDraw {PerFrame(Part.EndDraw):0.000}, colocar {PerFrame(Part.Present):0.000} ms; {_primitives / (double)Math.Max(1, _frames):0.0} figuras, {_geometries / (double)Math.Max(1, _frames):0.0} geometrías nuevas y {_layers / (double)Math.Max(1, _frames):0.0} capas por fotograma; {(GC.GetAllocatedBytesForCurrentThread() - _allocated) / 1024.0 / Math.Max(1, _ticks):0.0} KB por ciclo, {(GC.CollectionCount(0) - _collections) / seconds:0.00} recolecciones/s"));
        _allocated = GC.GetAllocatedBytesForCurrentThread();
        _collections = GC.CollectionCount(0);
        Array.Clear(Totals);
        _wakes = _ticks = _frames = _geometries = _layers = _primitives = 0;
        _since = now;
    }
}
