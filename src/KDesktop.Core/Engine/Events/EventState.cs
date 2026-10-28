namespace KDesktop.Core.Engine.Events;

/// <summary>Why an event did not run when its moment came (B3, 5.0.345): what the assistants answer «¿por qué no saltó?» with.</summary>
public enum SkipReason { Paused, Condition, Cooldown, Suspended, Loop, Missed }

/// <param name="Time">When (UTC), the last time.</param>
/// <param name="Times">How many times in a row for the same reason (an event every minute does not fill the list).</param>
/// <param name="Detail">The condition not met («Horario: 9:00–14:00»), the time missed…</param>
public sealed record EventSkip(DateTime Time, string Id, string Name, SkipReason Reason, string? Detail = null, int Times = 1);

/// <summary>
/// What the events remember between runs (events-state.json, not config.json, so a firing doesn't rewrite the
/// configuration): when each one last fired and until when K-Desktop was watching (the times missed while it was
/// closed or the PC asleep are counted from there).
/// </summary>
public sealed class EventState : IDisposable
{
    private sealed class Data
    {
        public DateTime? LastSeen { get; set; }
        public Dictionary<string, DateTime> LastFired { get; set; } = new(StringComparer.Ordinal);
        public List<EventSkip> Skips { get; set; } = [];
    }

    /// <summary>Skips kept (the newest).</summary>
    public const int MaxSkips = 200;

    private readonly Lock _lock = new();
    private readonly Timer _saveTimer;
    private readonly string _file;
    private Data _data = new();

    public EventState(string file)
    {
        _file = file;
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            if (File.Exists(file)) _data = Json.Deserialize<Data>(File.ReadAllText(file)) ?? new Data();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo leer el estado de los eventos: " + ex.Message);
        }
    }

    public static string DefaultFile => Path.Combine(AppPaths.DataDir, "events-state.json");

    /// <summary>Until when K-Desktop was watching the last time (UTC); null the first time.</summary>
    public DateTime? LastSeen
    {
        get { lock (_lock) return _data.LastSeen; }
    }

    public DateTime? LastFired(string id)
    {
        lock (_lock) return _data.LastFired.TryGetValue(id, out var t) ? t : null;
    }

    public void SetFired(string id, DateTime utc)
    {
        lock (_lock) _data.LastFired[id] = utc;
        SaveSoon();
    }

    /// <summary>The times events did not run, oldest first.</summary>
    public IReadOnlyList<EventSkip> Skips
    {
        get { lock (_lock) return [.. _data.Skips]; }
    }

    /// <summary>An event did not run when its moment came.</summary>
    public void Skipped(EventSkip skip)
    {
        lock (_lock)
        {
            // The same again: one entry, counted, at its last time.
            var same = _data.Skips.FindIndex(s => s.Id == skip.Id && s.Reason == skip.Reason && s.Detail == skip.Detail);
            if (same >= 0)
            {
                skip = skip with { Times = _data.Skips[same].Times + 1 };
                _data.Skips.RemoveAt(same);
            }
            _data.Skips.Add(skip);
            if (_data.Skips.Count > MaxSkips) _data.Skips.RemoveRange(0, _data.Skips.Count - MaxSkips);
        }
        SaveSoon();
    }

    /// <summary>K-Desktop is watching now (a heartbeat while there are times to catch up).</summary>
    public void Seen(DateTime utc, bool save)
    {
        lock (_lock) _data.LastSeen = utc;
        if (save) SaveSoon();
    }

    /// <summary>Forgets events that no longer exist.</summary>
    public void Prune(IEnumerable<string> ids)
    {
        var keep = ids.ToHashSet(StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var id in _data.LastFired.Keys.Where(id => !keep.Contains(id)).ToList()) _data.LastFired.Remove(id);
        }
    }

    private void SaveSoon() => _saveTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

    public void Save()
    {
        string json;
        lock (_lock) json = Json.Serialize(_data);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo guardar el estado de los eventos: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Save();
    }
}
