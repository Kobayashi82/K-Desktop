namespace KDesktop.Core.Engine;

/// <summary>How a run ended.</summary>
public enum RunOutcome { Ok, Error, Cancelled, Stopped, NotUnderstood }

/// <summary>
/// One line of the «Registro»: what ran, from where, when, how long it took and how it went.
/// </summary>
/// <param name="CommandId">Null for what has no command (a voice order not understood).</param>
/// <param name="Detail">More about the origin: the event, what the voice heard, the device of the web, the UDP sender…</param>
/// <param name="Message">Why it failed or stopped (the first error, the condition not met…).</param>
/// <param name="Depth">0 for a run started from outside; 1+ for a command another command ran.</param>
public sealed record HistoryEntry(
    DateTime Time,
    string? CommandId,
    string Name,
    TriggerSource Source,
    string? Detail,
    RunOutcome Outcome,
    string? Message,
    int Milliseconds,
    int Depth = 0);

/// <summary>
/// The last runs of commands (and voice orders not understood), newest last, kept in history.json so they survive a
/// restart. Thread-safe.
/// </summary>
public sealed class ExecutionHistory : IDisposable
{
    public const int Capacity = 500;

    private static readonly AsyncLocal<string?> AmbientDetail = new();

    private readonly Lock _lock = new();
    private readonly List<HistoryEntry> _entries = [];
    private readonly string? _file;
    private readonly Timer? _saveTimer;

    /// <param name="file">Where it is kept; null = only in memory (tests).</param>
    public ExecutionHistory(string? file)
    {
        _file = file;
        if (file is null) return;
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            if (File.Exists(file) && Json.Deserialize<List<HistoryEntry>>(File.ReadAllText(file)) is { } saved)
                _entries.AddRange(saved.TakeLast(Capacity));
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo leer el registro de ejecuciones: " + ex.Message);
        }
    }

    public static string DefaultFile => Path.Combine(AppPaths.DataDir, "history.json");

    /// <summary>Raised (on any thread) after an entry is added, or with null after <see cref="Clear"/>.</summary>
    public event Action<HistoryEntry?>? Changed;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    /// <summary>
    /// What the next runs started inside the returned scope say about their origin («Evento «Juegos»», «apaga la
    /// luz»…); it flows with the execution.
    /// </summary>
    public static IDisposable Describe(string? detail)
    {
        var previous = AmbientDetail.Value;
        AmbientDetail.Value = detail;
        return new Restore(previous);
    }

    /// <summary>The origin set with <see cref="Describe"/> for the current flow.</summary>
    public static string? CurrentDetail => AmbientDetail.Value;

    public void Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        }
        _saveTimer?.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        Changed?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        _saveTimer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        Changed?.Invoke(null);
    }

    public void Save()
    {
        if (_file is null) return;
        string json;
        lock (_lock) json = Json.Serialize(_entries);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo guardar el registro de ejecuciones: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        Save();
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => AmbientDetail.Value = previous;
    }
}
