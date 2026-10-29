using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>What a watch waits for (stage 8, <c>avisame_cuando</c>).</summary>
public static class WatchKinds
{
    public const string Closes = "closes", Appears = "appears", CpuBelow = "cpu_below", GpuBelow = "gpu_below",
        Download = "download", FolderQuiet = "folder_quiet";

    public static readonly IReadOnlyList<string> All = [Closes, Appears, CpuBelow, GpuBelow, Download, FolderQuiet];
}

/// <summary>
/// A watch: something on the PC to tell the user about when it happens, with what it was resolved to when it was set
/// (the process or the very window, the windows already open, the partial downloads…).
/// </summary>
/// <param name="What">In the user's words (the program, the folder…), for saying it.</param>
/// <param name="Assistant">Who set it (it tells you); null: K-Desktop.</param>
public sealed record WatchItem(string Id, string Kind, string What, string? Assistant, DateTimeOffset Since)
{
    /// <summary>Closes: the process to wait for (all of its processes gone).</summary>
    public string? Process { get; init; }

    /// <summary>Closes: the very window to wait for (a tab of Chrome, not all of Chrome).</summary>
    public long? Window { get; init; }

    /// <summary>Appears: the windows there already, which do not count.</summary>
    public long[]? Known { get; init; }

    /// <summary>CPU or GPU below this %.</summary>
    public int? Percent { get; init; }

    /// <summary>Download (its files) or folder that stops changing.</summary>
    public string? Folder { get; init; }

    /// <summary>Download: the partial files (.crdownload, .part…) that must be gone.</summary>
    public string[]? Partials { get; init; }

    /// <summary>Download: no partial files, so the network going quiet (Steam, a launcher).</summary>
    public bool Network { get; init; }

    /// <summary>Download by the network: bytes per second when it was set (the quiet is measured against it).</summary>
    public long? Rate { get; init; }

    /// <summary>Also told on the phone (B11): «avísame en el móvil cuando termine la descarga».</summary>
    public bool Phone { get; init; }
}

/// <summary>What the watches look at, so tests can pretend.</summary>
public interface IWatchProbe
{
    /// <summary>Normalised names of the running processes.</summary>
    IReadOnlySet<string> Processes();

    /// <summary>A running process by the user's words, or null.</summary>
    string? FindProcess(string said);

    IReadOnlyList<OpenWindow> Windows();

    bool WindowExists(long hwnd);

    /// <summary>% in use since the previous reading.</summary>
    int Load(LoadResource resource);

    /// <summary>Bytes received by the network so far.</summary>
    long Received();

    /// <summary>The partial downloads in a folder (full paths).</summary>
    IReadOnlyList<string> Partials(string folder);

    /// <summary>A stamp of a folder's contents (changes when anything in it does), or null when it cannot be read.</summary>
    string? Stamp(string folder);

    /// <summary>The newest file of a folder written after a moment, or null.</summary>
    string? Newest(string folder, DateTimeOffset after);
}

/// <summary>The real PC.</summary>
public sealed class SystemProbe : IWatchProbe, IDisposable
{
    private readonly SystemLoad _cpu = new(), _gpu = new();
    private readonly object _gate = new();

    /// <summary>Files a browser writes while downloading (Chrome/Edge, Firefox, old Edge, Safari, Opera).</summary>
    public static readonly IReadOnlyList<string> PartialExtensions = [".crdownload", ".part", ".partial", ".download", ".opdownload"];

    public IReadOnlySet<string> Processes() => ProcessSnapshot.Names();

    public string? FindProcess(string said) => AppFinder.FindProcess(said);

    public IReadOnlyList<OpenWindow> Windows() => AppFinder.OpenWindows();

    public bool WindowExists(long hwnd) => IsWindow((IntPtr)hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    public int Load(LoadResource resource)
    {
        lock (_gate) return (resource == LoadResource.Gpu ? _gpu : _cpu).Read(resource);
    }

    public long Received() => NetworkInfo.Traffic().Down;

    public IReadOnlyList<string> Partials(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFiles(folder).Where(f => PartialExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Count, size and last write of what is in it (5 levels, 20 000 entries as most).</summary>
    public string? Stamp(string folder)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, AttributesToSkip = 0 };
            long count = 0, size = 0, latest = 0;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                count++;
                size += file.Length;
                latest = Math.Max(latest, file.LastWriteTimeUtc.Ticks);
                if (count >= 20_000) break;
            }
            return string.Create(CultureInfo.InvariantCulture, $"{count}/{size}/{latest}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public string? Newest(string folder, DateTimeOffset after)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => f.LastWriteTimeUtc >= after.UtcDateTime.AddSeconds(-5) && !PartialExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Dispose()
    {
        _cpu.Dispose();
        _gpu.Dispose();
    }
}

/// <summary>
/// The watches of all the assistants (stage 8, <c>avisame_cuando</c>): one look every <see cref="Every"/> while there are
/// any (a process snapshot and the windows: about a millisecond), kept in a file so they survive a restart. Each one is
/// told once (<see cref="Fired"/>, on a pool thread, with what happened: the window that appeared, the % reached, the
/// file downloaded) and goes; after <see cref="MaxAge"/> it goes untold. Thread-safe.
/// </summary>
public sealed class WatchStore : IDisposable
{
    public const int MaxPending = 10;

    public static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>The processor or the graphics card below the % this long (not one quiet moment).</summary>
    public static readonly TimeSpan Sustain = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A download by the network is over when, this long, it receives on average less than <see cref="QuietRate"/> or a
    /// tenth of what it was downloading at (the rest of the PC keeps using the network: a call, a remote desktop).
    /// </summary>
    public static readonly TimeSpan NetworkQuiet = TimeSpan.FromSeconds(20);

    /// <summary>A folder that has not changed this long has stopped changing.</summary>
    public static readonly TimeSpan FolderQuiet = TimeSpan.FromSeconds(30);

    /// <summary>Bytes per second: more is a download going on, less is the network at rest.</summary>
    public const long DownloadRate = 300 * 1024, QuietRate = 60 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly object _gate = new();
    private readonly string _file;
    private readonly ITimer _timer;
    private readonly Dictionary<string, Progress> _progress = new(StringComparer.Ordinal);
    private List<WatchItem> _items;
    private int _checking;

    /// <summary>What a watch has seen so far (not saved: it starts again after a restart).</summary>
    private sealed class Progress
    {
        public DateTimeOffset? Since;
        public string? Stamp;
        public long? Received;
        public DateTimeOffset At;

        /// <summary>What had been received when it went quiet (<see cref="Since"/>).</summary>
        public long SinceReceived;
    }

    public WatchStore(string file, IWatchProbe probe, TimeProvider? time = null)
    {
        _file = file;
        Probe = probe;
        Time = time ?? TimeProvider.System;
        _items = Load(file);
        _timer = Time.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A watch happened (and was removed); the text says what, for <see cref="Told"/>.</summary>
    public event Action<WatchItem, string?>? Fired;

    public IWatchProbe Probe { get; }

    public TimeProvider Time { get; }

    public IReadOnlyList<WatchItem> Pending
    {
        get { lock (_gate) return [.. _items]; }
    }

    /// <summary>Starts looking (also at those kept from before).</summary>
    public void Start() => Schedule();

    /// <summary>Adds one already resolved (<see cref="NotifyWhenTool"/>); the reason when it cannot.</summary>
    public string? Add(WatchItem item)
    {
        lock (_gate)
        {
            if (_items.Count >= MaxPending) return $"There are already {MaxPending} things being watched.";
            _items.Add(item);
        }
        Changed();
        return null;
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(w => w.Id == id) > 0;
            _progress.Remove(id);
        }
        if (removed) Changed();
        return removed;
    }

    /// <summary>The one whose words best match <paramref name="hint"/> (or the newest when it says nothing), removed.</summary>
    public WatchItem? RemoveMatching(string? hint)
    {
        WatchItem? found;
        lock (_gate)
        {
            var words = Words(hint);
            found = _items.AsEnumerable().Reverse()
                .Select(w => (Item: w, Score: words.Length == 0 ? 1 : words.Count(Words(w.What + " " + Told(w)).Contains)))
                .Where(p => p.Score > 0)
                .OrderByDescending(p => p.Score)
                .Select(p => p.Item)
                .FirstOrDefault();
            if (found is not null)
            {
                _items.Remove(found);
                _progress.Remove(found.Id);
            }
        }
        if (found is not null) Changed();
        return found;
    }

    private static string[] Words(string? text) => Core.Engine.FuzzyMatch.Tokens(text ?? string.Empty).Where(w => w.Length > 2).ToArray();

    /// <summary>One look at everything watched (the timer calls it; tests too).</summary>
    public void Check()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            var now = Time.GetLocalNow();
            List<WatchItem> items;
            lock (_gate) items = [.. _items];
            var fired = new List<(WatchItem Item, string? Detail)>();
            var expired = new List<WatchItem>();
            IReadOnlySet<string>? processes = null;
            IReadOnlyList<OpenWindow>? windows = null;
            int? cpu = null, gpu = null;
            long? received = null;
            foreach (var item in items)
            {
                if (now - item.Since > MaxAge)
                {
                    expired.Add(item);
                    continue;
                }
                Progress progress;
                lock (_gate)
                {
                    if (!_progress.TryGetValue(item.Id, out progress!)) _progress[item.Id] = progress = new Progress { At = now, Since = item.Kind == WatchKinds.FolderQuiet ? now : null };
                }
                try
                {
                    var (happened, detail) = item.Kind switch
                    {
                        WatchKinds.Closes when item.Window is { } hwnd => (!Probe.WindowExists(hwnd), null),
                        WatchKinds.Closes => (!(processes ??= Probe.Processes()).Contains(item.Process ?? string.Empty), null),
                        WatchKinds.Appears => Appeared(item, windows ??= Probe.Windows()),
                        WatchKinds.CpuBelow => Below(item, progress, cpu ??= Probe.Load(LoadResource.Cpu), now),
                        WatchKinds.GpuBelow => Below(item, progress, gpu ??= Probe.Load(LoadResource.Gpu), now),
                        WatchKinds.Download when item.Network => Quiet(progress, received ??= Probe.Received(), now, QuietFor(item)),
                        WatchKinds.Download => Downloaded(item),
                        WatchKinds.FolderQuiet => Settled(item, progress, now),
                        _ => (false, null),
                    };
                    if (happened) fired.Add((item, detail));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Asistente: no se ha podido mirar «{item.What}» ({item.Kind}): {ex.Message}");
                }
            }
            if (fired.Count + expired.Count > 0)
            {
                lock (_gate)
                {
                    foreach (var gone in fired.Select(f => f.Item).Concat(expired))
                    {
                        _items.Remove(gone);
                        _progress.Remove(gone.Id);
                    }
                }
                Save();
            }
            foreach (var gone in expired) Log.Info($"Asistente: deja de vigilar «{gone.What}» ({gone.Kind}): lleva un día");
            foreach (var (item, detail) in fired)
            {
                try { Fired?.Invoke(item, detail); }
                catch (Exception ex) { Log.Warn($"Asistente: aviso fallido: {ex.Message}"); }
            }
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
            Schedule();
        }
    }

    private static (bool, string?) Appeared(WatchItem item, IReadOnlyList<OpenWindow> windows)
    {
        var known = item.Known ?? [];
        var fresh = windows.Where(w => !known.Contains((long)w.Hwnd)).ToList();
        return AppFinder.FindWindow(item.What, fresh) is { } window ? (true, window.DisplayName) : (false, null);
    }

    private static (bool, string?) Below(WatchItem item, Progress progress, int load, DateTimeOffset now)
    {
        if (load > (item.Percent ?? 20))
        {
            progress.Since = null;
            return (false, null);
        }
        progress.Since ??= now;
        return now - progress.Since >= Sustain ? (true, load.ToString(CultureInfo.InvariantCulture)) : (false, null);
    }

    /// <summary>Bytes per second under which the network counts as quiet for that download.</summary>
    public static long QuietFor(WatchItem item) => Math.Max(QuietRate, (item.Rate ?? 0) / 10);

    /// <summary>
    /// Quiet when it received less than <paramref name="quiet"/> since the last reading and, on average, since it went
    /// quiet: a moment of more traffic from something else does not start it all over again; a download that comes back does.
    /// </summary>
    private static (bool, string?) Quiet(Progress progress, long received, DateTimeOffset now, long quiet)
    {
        var (before, at) = (progress.Received, progress.At);
        progress.Received = received;
        progress.At = now;
        if (before is not { } last || now <= at) return (false, null);
        if (progress.Since is not { } since)
        {
            if ((received - last) / (now - at).TotalSeconds >= quiet) return (false, null);
            progress.Since = at;
            progress.SinceReceived = last;
            return (false, null);
        }
        if ((received - progress.SinceReceived) / (now - since).TotalSeconds >= quiet)
        {
            progress.Since = null;
            return (false, null);
        }
        return (now - since >= NetworkQuiet, null);
    }

    private (bool, string?) Downloaded(WatchItem item)
    {
        var folder = item.Folder ?? string.Empty;
        var now = Probe.Partials(folder);
        if ((item.Partials ?? []).Any(p => now.Contains(p, StringComparer.OrdinalIgnoreCase))) return (false, null);
        return (true, Probe.Newest(folder, item.Since));
    }

    private (bool, string?) Settled(WatchItem item, Progress progress, DateTimeOffset now)
    {
        var stamp = Probe.Stamp(item.Folder ?? string.Empty);
        if (stamp != progress.Stamp)
        {
            progress.Stamp = stamp;
            progress.Since = now;
            return (false, null);
        }
        return (now - progress.Since >= FolderQuiet, null);
    }

    private void Changed()
    {
        Save();
        Schedule();
    }

    private void Schedule()
    {
        bool any;
        lock (_gate) any = _items.Count > 0;
        _timer.Change(any ? Every : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private static List<WatchItem> Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<List<WatchItem>>(File.ReadAllText(file), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido leer los avisos pendientes: {ex.Message}");
            return [];
        }
    }

    private void Save()
    {
        string json;
        lock (_gate) json = JsonSerializer.Serialize(_items, Json);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido guardar los avisos pendientes: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        (Probe as IDisposable)?.Dispose();
    }

    // ── words for people ─────────────────────────────────────────────────

    /// <summary>What it waits for, in the user's language («cuando se cierre Blender»).</summary>
    public static string Told(WatchItem item) => item.Kind switch
    {
        WatchKinds.CpuBelow or WatchKinds.GpuBelow => Loc.T("watch." + item.Kind, item.Percent ?? 20),
        WatchKinds.Download => Loc.T("watch.download"),
        _ => Loc.T("watch." + item.Kind, item.What),
    };

    /// <summary>What happened, in the user's language («Blender se ha cerrado.»).</summary>
    public static string Happened(WatchItem item, string? detail) => item.Kind switch
    {
        WatchKinds.Appears => Loc.T("watch.fired.appears", detail ?? item.What),
        WatchKinds.CpuBelow or WatchKinds.GpuBelow => Loc.T("watch.fired." + item.Kind, detail ?? (item.Percent ?? 20).ToString(CultureInfo.InvariantCulture)),
        WatchKinds.Download => detail is { Length: > 0 } file ? Loc.T("watch.fired.downloadOf", file) : Loc.T("watch.fired.download"),
        _ => Loc.T("watch.fired." + item.Kind, item.What),
    };
}

/// <summary>
/// «avísame cuando termine la descarga», «dime cuando se cierre Blender», «avísame cuando baje la CPU» (stage 8,
/// <c>avisame_cuando</c>): resolves what to wait for now (and says so when it is not there or already happened) and
/// leaves it to the <see cref="WatchStore"/>.
/// </summary>
public sealed class NotifyWhenTool(WatchStore watches) : ITool
{
    /// <summary>How long the network is measured to see whether something is downloading.</summary>
    private static readonly TimeSpan MeasureFor = TimeSpan.FromSeconds(2);

    public string Name => "notify_when";

    public string Description => "Tells the user later, when something happens on the PC: closes (a program or window closes), appears (a window of a program appears), cpu_below or gpu_below (the processor or graphics card goes below a %), download (the download in progress finishes: browser or Steam), folder_quiet (a folder stops changing: a copy or export ends). For a time or a delay use set_timer.";

    public string ParametersSchemaJson { get; } =
        $$$$"""{"type":"object","properties":{"when":{"type":"string","enum":[{{{{string.Join(",", WatchKinds.All.Select(k => $"\"{k}\""))}}}}]},"what":{"type":"string","description":"The program, window or folder, in the user's words (for closes, appears, folder_quiet; a folder can be downloads, desktop, documents… or a path)"},"percent":{"type":"integer","description":"For cpu_below / gpu_below: the % (default 20)"},"phone":{"type":"boolean","description":"Also tell it on the user's phone (when they say so: on the phone, al móvil)"}},"required":["when"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) =>
        Loc.T("tool.notify_when.intent", Kind(arguments) is { } kind ? WatchStore.Told(Draft(kind, arguments)) : Loc.T("tool.notify_when.something"));

    private static string? Kind(JsonElement arguments) =>
        arguments.GetString("when")?.Trim().ToLowerInvariant() switch
        {
            { } kind when WatchKinds.All.Contains(kind) => kind,
            "close" or "closed" or "exits" => WatchKinds.Closes,
            "appear" or "opens" or "open" => WatchKinds.Appears,
            "cpu" => WatchKinds.CpuBelow,
            "gpu" => WatchKinds.GpuBelow,
            "download_done" or "download_finished" or "downloads" => WatchKinds.Download,
            "folder" => WatchKinds.FolderQuiet,
            _ => null,
        };

    private static WatchItem Draft(string kind, JsonElement arguments, string? assistant = null, DateTimeOffset since = default) =>
        new(Guid.NewGuid().ToString("N")[..10], kind, ToolText.Clean(arguments.GetString("what")), assistant, since)
        {
            Percent = kind is WatchKinds.CpuBelow or WatchKinds.GpuBelow ? (int)Math.Clamp(Math.Round(arguments.GetNumber("percent") ?? 20), 1, 95) : null,
        };

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (Kind(arguments) is not { } kind) return ToolResult.Fail("Say when: one of " + string.Join(", ", WatchKinds.All) + ".");
        var item = Draft(kind, arguments, context.Assistant, watches.Time.GetLocalNow()) with
        {
            Phone = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("phone", out var phone) && phone.ValueKind == JsonValueKind.True,
        };
        var probe = watches.Probe;
        string? note = null;
        long watched = 0;
        switch (kind)
        {
            case WatchKinds.Closes:
            {
                if (item.What.Length == 0) return ToolResult.Fail("Say which program or window.");
                var window = AppFinder.FindWindow(item.What, probe.Windows());
                var process = probe.FindProcess(item.What);
                if (window is null && process is null)
                    return ToolResult.Fail($"\"{item.What}\" is not open. Say so.") with { Reply = Loc.T("act.reason.noProcess", item.What) };
                // The words name the program: all of it; else that window (a tab, a document).
                item = process is not null && (window is null || window.Process == process)
                    ? item with { Process = process }
                    : item with { Window = (long)window!.Hwnd };
                watched = window is null ? 0 : (long)window.Hwnd;
                break;
            }
            case WatchKinds.Appears:
            {
                if (item.What.Length == 0) return ToolResult.Fail("Say which program or window.");
                var windows = probe.Windows();
                item = item with { Known = [.. windows.Select(w => (long)w.Hwnd)] };
                if (AppFinder.FindWindow(item.What, windows) is { } open) note = $"(A window of it is already open: {open.DisplayName}; a new one will be told.)";
                break;
            }
            case WatchKinds.CpuBelow or WatchKinds.GpuBelow:
            {
                var resource = kind == WatchKinds.CpuBelow ? LoadResource.Cpu : LoadResource.Gpu;
                var now = await Task.Run(() => probe.Load(resource), cancellationToken).ConfigureAwait(false);
                if (now <= item.Percent)
                {
                    var already = Loc.T("watch.already." + kind, now);
                    return new ToolResult($"It is already at {now}%, below {item.Percent}%: nothing to wait for. Say so.") { Done = already, Reply = already };
                }
                note = $"(Now it is at {now}%.)";
                break;
            }
            case WatchKinds.Download:
            {
                var folder = UserFolders.Find(arguments.GetString("what")) ?? UserFolders.Find("downloads");
                var partials = folder is null ? [] : probe.Partials(folder);
                if (partials.Count > 0)
                {
                    item = item with { Folder = folder, Partials = [.. partials], What = string.Empty };
                    note = $"(Downloading: {string.Join(", ", partials.Select(Path.GetFileNameWithoutExtension))}.)";
                    break;
                }
                var before = probe.Received();
                var measuring = System.Diagnostics.Stopwatch.StartNew();
                // Two seconds: Windows updates the figures by jumps, and over one second a 42 MB/s download looked like 200.
                await Task.Delay(MeasureFor, cancellationToken).ConfigureAwait(false);
                var rate = (probe.Received() - before) / Math.Max(0.1, measuring.Elapsed.TotalSeconds);
                if (rate < WatchStore.DownloadRate)
                {
                    var none = Loc.T("watch.noDownload");
                    return ToolResult.Fail("No download is going on (nothing being downloaded in the browser, and the network is quiet). Say so.") with { Reply = none };
                }
                item = item with { Network = true, What = string.Empty, Rate = (long)rate };
                note = string.Create(CultureInfo.InvariantCulture, $"(Downloading at {rate / (1024 * 1024):0.#} MB/s.)");
                break;
            }
            case WatchKinds.FolderQuiet:
            {
                var folder = UserFolders.Find(arguments.GetString("what"));
                if (folder is null)
                    return ToolResult.Fail($"There is no folder \"{item.What}\": give one of {string.Join(", ", UserFolders.Names)} or a full path.") with { Reply = Loc.T("watch.noFolder") };
                item = item with { Folder = folder, What = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : folder };
                break;
            }
        }
        if (watches.Add(item) is { } error) return ToolResult.Fail(error);
        // A beacon on the window it keeps an eye on.
        if (watched != 0) context.Show?.Invoke(ToolGesture.Mark(ToolSpot.Window, window: watched));
        var told = WatchStore.Told(item);
        return new ToolResult($"Watching: the user will be told {kind switch
        {
            WatchKinds.Closes => $"when {item.What} closes",
            WatchKinds.Appears => $"when a window of {item.What} appears",
            WatchKinds.CpuBelow => $"when the processor stays below {item.Percent}%",
            WatchKinds.GpuBelow => $"when the graphics card stays below {item.Percent}%",
            WatchKinds.Download => "when the download finishes",
            _ => $"when {item.What} stops changing",
        }}{(item.Phone ? ", also on their phone" : string.Empty)}. {note}".Trim())
        {
            Done = Loc.T("tool.notify_when.done", told),
            Reply = Loc.T(item.Phone ? "tool.notify_when.replyPhone" : "tool.notify_when.reply", told),
            Undo = new ToolUndo(Name, item.Id),
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(watches.Remove(undo.Data));
}

/// <summary>«Ya no hace falta que me avises».</summary>
public sealed class StopWatchingTool(WatchStore watches) : ITool
{
    public string Name => "stop_watching";

    public string Description => "Stops a pending notify_when: the one whose words match, or the latest one.";

    public string ParametersSchemaJson => ToolText.Optional(("what", "string", "Words of what was being watched, to find it (optional)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.stop_watching.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (watches.RemoveMatching(arguments.GetString("what")) is not { } removed)
        {
            var pending = watches.Pending;
            return Task.FromResult(ToolResult.Fail(pending.Count == 0 ? "Nothing is being watched." : "None matches. Being watched: " + string.Join("; ", pending.Select(WatchStore.Told)) + ".")
                with { Reply = pending.Count == 0 ? Loc.T("tool.stop_watching.none") : null });
        }
        var told = WatchStore.Told(removed);
        return Task.FromResult(new ToolResult($"No longer watching: {removed.Kind} {removed.What}.")
        {
            Done = Loc.T("tool.stop_watching.done", told),
            Reply = Loc.T("tool.stop_watching.reply", told),
            Undo = new ToolUndo(Name, JsonSerializer.Serialize(removed)),
        });
    }

    /// <summary>Puts it back.</summary>
    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) =>
        Task.FromResult(JsonSerializer.Deserialize<WatchItem>(undo.Data) is { } item && watches.Add(item) is null);
}
