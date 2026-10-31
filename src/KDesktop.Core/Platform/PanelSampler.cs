using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>
/// Reads the figures of the panels («Paneles»), one reading for all of them: only the kinds some shown panel uses, every
/// <see cref="Interval"/> on a pool thread, and keeps a short history of each for the graphs. Nothing to read: stopped.
/// </summary>
public sealed class PanelSampler : IDisposable
{
    /// <summary>The points a graph has (the last ones).</summary>
    public const int HistoryLength = 60;

    private readonly Lock _gate = new();
    private readonly Dictionary<PanelItemKind, double?> _values = [];
    private readonly Dictionary<PanelItemKind, Queue<double>> _history = [];
    private readonly SystemLoad _load = new();
    private readonly GpuTemperature _gpuTemperature = new();
    private HashSet<PanelItemKind> _wanted = [];
    private Timer? _timer;
    private int _busy;
    private (long Down, long Up, long Ticks)? _traffic;

    /// <summary>The adapters counted for the network, found again only when the network changes (or every 5 minutes).</summary>
    private NetworkInterface[]? _adapters;
    private long _adaptersFound;

    public PanelSampler() => NetworkChange.NetworkAddressChanged += OnNetworkChanged;

    private void OnNetworkChanged(object? sender, EventArgs e) => Volatile.Write(ref _adapters, null);

    /// <summary>Milliseconds between two readings (that of the quickest panel).</summary>
    public int Interval { get; private set; } = 1000;

    /// <summary>After each reading, on the pool thread.</summary>
    public event EventHandler? Sampled;

    /// <summary>What to read and how often; nothing: it stops.</summary>
    public void Want(IEnumerable<PanelItemKind> kinds, int interval)
    {
        var wanted = kinds.Where(k => k != PanelItemKind.Spacer).ToHashSet();
        interval = Math.Clamp(interval, 250, 60000);
        lock (_gate)
        {
            if (wanted.SetEquals(_wanted) && interval == Interval && (_timer is not null) == (wanted.Count > 0)) return;
            _wanted = wanted;
            Interval = interval;
            foreach (var gone in _values.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                _values.Remove(gone);
                _history.Remove(gone);
            }
            if (!wanted.Contains(PanelItemKind.NetUp) && !wanted.Contains(PanelItemKind.NetDown)) _traffic = null;
            if (wanted.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }
            if (_timer is null) _timer = new Timer(_ => Sample(), null, 0, interval);
            else _timer.Change(interval, interval);
        }
    }

    /// <summary>The last figure of <paramref name="kind"/>; null while there is none (or it can't be read on this PC).</summary>
    public double? Value(PanelItemKind kind)
    {
        lock (_gate) return _values.GetValueOrDefault(kind);
    }

    /// <summary>The last figures of <paramref name="kind"/>, oldest first.</summary>
    public double[] History(PanelItemKind kind)
    {
        lock (_gate) return _history.TryGetValue(kind, out var points) ? [.. points] : [];
    }

    private void Sample()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            HashSet<PanelItemKind> wanted;
            lock (_gate) wanted = _wanted;
            var read = new Dictionary<PanelItemKind, double?>();
            if (wanted.Contains(PanelItemKind.NetUp) || wanted.Contains(PanelItemKind.NetDown))
            {
                var adapters = Volatile.Read(ref _adapters);
                if (adapters is null || Stopwatch.GetElapsedTime(_adaptersFound).TotalMinutes >= 5)
                {
                    // Other adapters: other totals, so no speed until the next reading.
                    _adapters = adapters = NetworkInfo.CountedAdapters();
                    _adaptersFound = Stopwatch.GetTimestamp();
                    _traffic = null;
                }
                var (down, up) = NetworkInfo.Sum(adapters);
                var now = Stopwatch.GetTimestamp();
                if (_traffic is { } before && now > before.Ticks)
                {
                    var seconds = (now - before.Ticks) / (double)Stopwatch.Frequency;
                    read[PanelItemKind.NetDown] = Math.Max(0, (down - before.Down) / seconds);
                    read[PanelItemKind.NetUp] = Math.Max(0, (up - before.Up) / seconds);
                }
                _traffic = (down, up, now);
            }
            if (wanted.Contains(PanelItemKind.Cpu)) read[PanelItemKind.Cpu] = _load.Read(LoadResource.Cpu);
            if (wanted.Contains(PanelItemKind.Ram)) read[PanelItemKind.Ram] = _load.Read(LoadResource.Memory);
            if (wanted.Contains(PanelItemKind.Gpu)) read[PanelItemKind.Gpu] = _load.Read(LoadResource.Gpu);
            if (wanted.Contains(PanelItemKind.Vram)) read[PanelItemKind.Vram] = _load.Read(LoadResource.Vram);
            if (wanted.Contains(PanelItemKind.GpuTemp)) read[PanelItemKind.GpuTemp] = _gpuTemperature.Read();
            lock (_gate)
            {
                foreach (var (kind, value) in read)
                {
                    if (!_wanted.Contains(kind)) continue;
                    _values[kind] = value;
                    if (value is not { } v) continue;
                    if (!_history.TryGetValue(kind, out var points)) _history[kind] = points = new Queue<double>(HistoryLength);
                    if (points.Count == HistoryLength) points.Dequeue();
                    points.Enqueue(v);
                }
            }
            Sampled?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _adapters = null;
            Log.Warn("No se pudieron leer los datos de los paneles: " + ex.Message);
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>
    /// A figure as a panel shows it: «2.8 kB/s», «7%», «50°»; «—» while there is none. Speeds in units of 1024, with a
    /// decimal under 100.
    /// </summary>
    public static string Format(PanelItemKind kind, double? value, CultureInfo culture)
    {
        if (kind == PanelItemKind.Spacer) return string.Empty;
        if (value is not { } v) return "—";
        switch (kind)
        {
            case PanelItemKind.NetUp or PanelItemKind.NetDown:
                // From 0 it already says kB: bytes below 1 kB would make the text jump too much.
                string[] units = ["kB/s", "MB/s", "GB/s"];
                var unit = 0;
                v = Math.Max(0, v) / 1024;
                while (v >= 1000 && unit < units.Length - 1)
                {
                    v /= 1024;
                    unit++;
                }
                return v.ToString(v < 100 ? "0.0" : "0", culture) + " " + units[unit];
            case PanelItemKind.GpuTemp:
                return Math.Round(v).ToString(culture) + "°";
            default:
                return Math.Round(v).ToString(culture) + "%";
        }
    }

    /// <summary>The top of a graph: 100 for shares and degrees, the biggest of the history for speeds (at least 1 kB/s).</summary>
    public static double GraphTop(PanelItemKind kind, IReadOnlyList<double> points) =>
        kind is PanelItemKind.NetUp or PanelItemKind.NetDown ? Math.Max(1024, points.Count == 0 ? 0 : points.Max()) : 100;

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _wanted = [];
        }
        _load.Dispose();
        _gpuTemperature.Dispose();
    }
}
