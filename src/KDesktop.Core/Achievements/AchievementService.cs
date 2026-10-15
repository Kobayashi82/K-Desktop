using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Achievements;

/// <summary>
/// Keeps the statistics and the achievements (stats.json): counters, unlocking, and persistence
/// (debounced, atomic). Thread-safe: it is fed from the engine, the hooks and the UI.
/// </summary>
public sealed class AchievementService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Timer _saveTimer;
    private StatsData _data = new();
    private bool _dirty;

    /// <summary>Raised (on any thread) when an achievement is unlocked.</summary>
    public event Action<AchievementDef>? Unlocked;

    /// <summary>Raised (on any thread) after any statistic changes.</summary>
    public event Action? Changed;

    public AchievementService() => _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);

    // ── persistence ──────────────────────────────────────────────────────

    /// <summary>Loads stats.json. Returns false when it did not exist yet.</summary>
    public bool Load()
    {
        var exists = File.Exists(AppPaths.StatsFile);
        if (exists)
        {
            try
            {
                var data = Json.Deserialize<StatsData>(File.ReadAllText(AppPaths.StatsFile));
                if (data is not null) lock (_gate) _data = Normalize(data);
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo leer stats.json; se conserva una copia", ex);
                try { File.Copy(AppPaths.StatsFile, Path.Combine(AppPaths.BackupDir, $"stats.corrupt.{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); }
                catch { /* ignore */ }
            }
        }
        return exists;
    }

    private static StatsData Normalize(StatsData d)
    {
        // Dictionaries come back from JSON with the default comparer; keys are case-sensitive anyway.
        d.Counters ??= new();
        d.CommandRuns ??= new();
        d.RunsBySource ??= new();
        d.ActionKinds ??= [];
        d.Unlocked ??= new();
        if (d.FirstUse == default || d.FirstUse > DateTime.Now) d.FirstUse = DateTime.Now;
        return d;
    }

    public void SaveNow()
    {
        string json;
        lock (_gate)
        {
            if (!_dirty && File.Exists(AppPaths.StatsFile)) return;
            json = Json.Serialize(_data);
            _dirty = false;
        }
        try
        {
            AppPaths.EnsureCreated();
            var tmp = AppPaths.StatsFile + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(AppPaths.StatsFile)) File.Replace(tmp, AppPaths.StatsFile, null, ignoreMetadataErrors: true);
            else File.Move(tmp, AppPaths.StatsFile);
        }
        catch (Exception ex)
        {
            Log.Error("Error guardando las estadísticas", ex);
        }
    }

    public void Flush()
    {
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveNow();
    }

    private void Touch()
    {
        _dirty = true;
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    // ── queries ──────────────────────────────────────────────────────────

    public StatsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var all = AchievementCatalog.All;
            return new StatsSnapshot(_data.Clone(), all.Count(a => _data.Unlocked.ContainsKey(a.Id)), all.Count);
        }
    }

    public bool IsUnlocked(string id)
    {
        lock (_gate) return _data.Unlocked.ContainsKey(id);
    }

    /// <summary>Current value of the counter an achievement depends on.</summary>
    public static long Progress(StatsData data, AchievementDef def) => def.Stat switch
    {
        null => data.Unlocked.ContainsKey(def.Id) ? 1 : 0,
        Stat.Days => (long)Math.Floor((DateTime.Now - data.FirstUse).TotalDays),
        Stat.Kinds => data.ActionKinds.Count(k => Enum.TryParse<ActionKind>(k, out _)),
        _ => data.Get(def.Stat),
    };

    // ── updates ──────────────────────────────────────────────────────────

    public void Increment(string stat, long by = 1)
    {
        if (by == 0) return;
        List<AchievementDef> unlocked;
        lock (_gate)
        {
            _data.Counters[stat] = _data.Get(stat) + by;
            unlocked = Evaluate(stat);
            Touch();
        }
        Raise(unlocked);
    }

    /// <summary>Raises a counter to <paramref name="value"/> if it is higher (records: longest macro, deepest menu…).</summary>
    public void SetMax(string stat, long value)
    {
        List<AchievementDef> unlocked;
        lock (_gate)
        {
            if (value <= _data.Get(stat)) return;
            _data.Counters[stat] = value;
            unlocked = Evaluate(stat);
            Touch();
        }
        Raise(unlocked);
    }

    public void Unlock(string id)
    {
        if (AchievementCatalog.Get(id) is not { } def) return;
        List<AchievementDef> unlocked = [];
        lock (_gate)
        {
            if (!UnlockLocked(def, unlocked)) return;
            Touch();
        }
        Raise(unlocked);
    }

    /// <summary>A command finished: counters per command, per origin, and command-level achievements.</summary>
    public void CommandExecuted(Command command, TriggerSource source)
    {
        List<AchievementDef> unlocked;
        lock (_gate)
        {
            _data.Counters[Stat.CommandsRun] = _data.Get(Stat.CommandsRun) + 1;
            _data.CommandRuns[command.Id] = _data.CommandRuns.GetValueOrDefault(command.Id) + 1;
            var src = TriggerSourceNames.Key(source);
            _data.RunsBySource[src] = _data.RunsBySource.GetValueOrDefault(src) + 1;
            if (source == TriggerSource.Remote) _data.Counters[Stat.RemoteRun] = _data.Get(Stat.RemoteRun) + 1;

            unlocked = Evaluate(Stat.CommandsRun);
            unlocked.AddRange(Evaluate(Stat.RemoteRun));
            if (command.Actions.Count(a => a.Enabled) >= 5) UnlockLocked(AchievementCatalog.Get("multi")!, unlocked);
            if (DateTime.Now.Hour is >= 3 and < 5 && source != TriggerSource.Startup) UnlockLocked(AchievementCatalog.Get("night-owl")!, unlocked);
            Touch();
        }
        Raise(unlocked);
    }

    /// <summary>An action was performed inside a command.</summary>
    public void ActionExecuted(CommandAction action)
    {
        var stats = new List<string>(3) { Stat.ActionsRun };
        string? special = null;
        switch (action)
        {
            case RunAppAction: stats.Add(Stat.AppsLaunched); break;
            case OpenFolderAction { Special: "EmptyRecycleBin" }: stats.Add(Stat.RecycleEmptied); break;
            case OpenFolderAction f:
                stats.Add(Stat.FoldersOpened);
                if (f.Special == "KDesktopData") special = "curious";
                break;
            case OpenUrlAction: stats.Add(Stat.WebOpened); break;
            case RunScriptAction s:
                stats.Add(Stat.ScriptsRun);
                stats.Add(s.Shell == ScriptShell.Cmd ? Stat.ScriptsCmd : Stat.ScriptsPowerShell);
                break;
            case SendInputAction: stats.Add(Stat.KeysSent); break;
            case VolumeAction: stats.Add(Stat.VolumeChanged); break;
            case PlaySoundAction p:
                stats.Add(Stat.SoundsPlayed);
                if (File.Exists(p.Sound)) special = "custom-sound";
                break;
            case TopMostAction: stats.Add(Stat.TopMostToggled); break;
            case HiddenFilesAction: stats.Add(Stat.HiddenFilesToggled); break;
            case ToggleDisabledInAction: stats.Add(Stat.DisabledInToggled); break;
            case PowerAction p:
                stats.Add(Stat.PowerUsed);
                if (p.Mode == PowerMode.Shutdown) stats.Add(Stat.Shutdowns);
                break;
            case PlayMacroAction m:
                stats.Add(Stat.MacrosPlayed);
                if (m.UntilStopped) special = "macro-loop";
                break;
            case AudioOutputAction: stats.Add(Stat.AudioSwitched); break;
            case KBoardAction: stats.Add(Stat.KBoardToggled); break;
        }

        List<AchievementDef> unlocked = [];
        lock (_gate)
        {
            foreach (var s in stats)
            {
                _data.Counters[s] = _data.Get(s) + 1;
                unlocked.AddRange(Evaluate(s));
            }
            if (_data.ActionKinds.Add(action.Kind.ToString()))
            {
                if (AchievementCatalog.Get($"kind-{action.Kind}") is { } kind) UnlockLocked(kind, unlocked);
                unlocked.AddRange(Evaluate(Stat.Kinds));
            }
            if (special is not null) UnlockLocked(AchievementCatalog.Get(special)!, unlocked);
            Touch();
        }
        Raise(unlocked);

        // Shutting down: write now, the process may not get another chance.
        if (action is PowerAction) Flush();
    }

    /// <summary>Checks the achievements that depend on the configuration (number of commands, menu depth, chords).</summary>
    public void CheckConfig(AppConfig config)
    {
        SetMax(Stat.CommandsOwned, config.Commands.Count);
        var depth = config.QuickMenus.Count == 0 ? 0 : config.QuickMenus.Max(m => Depth(m.Items));
        SetMax(Stat.MenuDepth, depth);
        if (config.Commands.SelectMany(c => c.Hotkeys).Any(IsChord)) Unlock("chord");
        SetMax(Stat.MacroLongest, config.Macros.Count == 0 ? 0 : config.Macros.Max(m => m.Steps.Count));

        static int Depth(IEnumerable<QuickMenuItem> items) =>
            items.Select(i => i.Children.Count > 0 ? 1 + Depth(i.Children) : 1).DefaultIfEmpty(0).Max();
    }

    /// <summary>"Mouse 4 + Mouse 1": two or more mouse buttons, or buttons + wheel.</summary>
    private static bool IsChord(string hotkey)
    {
        var (_, main) = Hotkey.Parse(hotkey);
        return main.Split(Hotkey.Separator).Length >= 2;
    }

    /// <summary>Time-based achievements (called on start and periodically).</summary>
    public void CheckTime()
    {
        List<AchievementDef> unlocked;
        lock (_gate) unlocked = Evaluate(Stat.Days);
        if (unlocked.Count > 0)
        {
            lock (_gate) Touch();
            Raise(unlocked);
        }
    }

    /// <summary>Clears everything (statistics, achievements and the first-use date).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _data = new StatsData();
            _dirty = true;
        }
        Flush();
        Changed?.Invoke();
    }

    // ── internals (call with the lock held) ──────────────────────────────

    private List<AchievementDef> Evaluate(string stat)
    {
        var unlocked = new List<AchievementDef>();
        foreach (var def in AchievementCatalog.All)
        {
            if (def.Stat != stat || _data.Unlocked.ContainsKey(def.Id)) continue;
            if (Progress(_data, def) >= def.Target) UnlockLocked(def, unlocked);
        }
        return unlocked;
    }

    private bool UnlockLocked(AchievementDef def, List<AchievementDef> unlocked)
    {
        if (!_data.Unlocked.TryAdd(def.Id, DateTime.Now)) return false;
        unlocked.Add(def);
        if (def.Id != AchievementCatalog.First && AchievementCatalog.Get(AchievementCatalog.First) is { } first)
            UnlockLocked(first, unlocked);
        if (def.Id != AchievementCatalog.Master &&
            AchievementCatalog.All.All(a => a.Id == AchievementCatalog.Master || _data.Unlocked.ContainsKey(a.Id)))
            UnlockLocked(AchievementCatalog.Get(AchievementCatalog.Master)!, unlocked);
        return true;
    }

    private void Raise(List<AchievementDef> unlocked)
    {
        foreach (var def in unlocked)
        {
            Log.Info($"Logro desbloqueado: {def.Id}");
            try { Unlocked?.Invoke(def); }
            catch (Exception ex) { Log.Error("Error notificando un logro", ex); }
        }
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Error("Error notificando un cambio de estadísticas", ex); }
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }
}
