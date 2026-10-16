using System.Globalization;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine.Events;

/// <summary>
/// Watches what the events wait for and runs their commands. Only the detectors some enabled event needs are running
/// (no idle events, no idle timer…), so with no events it costs nothing. Everything happens off the UI and hook
/// threads. «Al iniciar / Al salir de K-Desktop» are not here: <see cref="CommandEngine.RunStartupCommandsAsync"/> and
/// <see cref="CommandEngine.RunExitCommandsAsync"/> run them at those moments.
/// </summary>
public sealed class EventService : IDisposable
{
    private static readonly TimeSpan ProcessPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(5), IdlePollActive = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxScheduleWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);

    /// <summary>A time noticed later than this (the PC was asleep, the clock moved) counts as missed.</summary>
    private static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(2);

    private const int ForegroundSettleMs = 300;

    private readonly CommandEngine _engine;
    private readonly IUiServices _ui;
    private readonly Func<AppConfig> _config;
    private readonly EventState _state;
    private readonly EventGuard _guard = new();
    private readonly Lock _lock = new();

    /// <summary>Enabled events by id, with their trigger as JSON (a changed trigger starts over).</summary>
    private Dictionary<string, (EventRule Rule, string Signature)> _rules = new(StringComparer.Ordinal);

    /// <summary>Events with an end whose start ran: their «Al terminar» commands are due.</summary>
    private readonly HashSet<string> _started = new(StringComparer.Ordinal);

    private bool _initial = true;
    private bool _afterUpdate;
    private bool _disposed;

    // «A una hora» and «Cada N minutos»
    private Timer? _scheduleTimer;
    private readonly Dictionary<string, DateTime> _due = new(StringComparer.Ordinal);
    private DateTime _lastTick;
    private DateTime _lastHeartbeat;

    // «Al abrir un programa»
    private Timer? _processTimer;
    private readonly ProcessTracker _processes = new();

    // «En primer plano»
    private SystemWatcher? _watcher;
    private Timer? _foregroundTimer;
    private readonly Dictionary<string, string[]> _foregroundGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _foregroundActive = new(StringComparer.Ordinal);

    // «Archivo nuevo en una carpeta»
    private readonly Dictionary<string, FileSystemWatcher> _fileWatchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _settling = new(StringComparer.OrdinalIgnoreCase);

    // «Sin usar el PC»
    private Timer? _idleTimer;
    private readonly Dictionary<string, bool> _idleActive = new(StringComparer.Ordinal);
    private bool _idleFast;

    public EventService(CommandEngine engine, IUiServices ui, Func<AppConfig> config, EventState? state = null)
    {
        _engine = engine;
        _ui = ui;
        _config = config;
        _state = state ?? new EventState(EventState.DefaultFile);
        _engine.EventFired += OnEventFired;
    }

    /// <summary>
    /// Starts watching. <paramref name="afterUpdate"/>: K-Desktop restarted to update itself, which is not a new start
    /// (programs already open don't count as «opening», even with that box ticked).
    /// </summary>
    public void Start(bool afterUpdate)
    {
        _afterUpdate = afterUpdate;
        foreach (var rule in _config().Events)
            if (_state.LastFired(rule.Id) is { } utc) rule.LastFired = utc.ToLocalTime();
        Rebuild();
    }

    /// <summary>Brings the detectors in line with the events (call after the configuration changes).</summary>
    public void Rebuild()
    {
        if (_disposed) return;
        var config = _config();
        lock (_lock)
        {
            var rules = new Dictionary<string, (EventRule, string)>(StringComparer.Ordinal);
            foreach (var rule in config.Events.ToArray())
                if (rule.Enabled) rules[rule.Id] = (rule, Json.Serialize(rule.Trigger));
            foreach (var (id, old) in _rules)
                if (!rules.TryGetValue(id, out var now) || now.Item2 != old.Signature) Forget(id);
            _rules = rules;

            ConfigureSchedule();
            ConfigureProcesses();
            ConfigureForeground();
            ConfigureIdle();
            ConfigureFiles();
            ConfigureWatcher();
            _initial = false;
        }
        _state.Prune(config.Events.Select(e => e.Id));
    }

    private void Forget(string id)
    {
        _started.Remove(id);
        _due.Remove(id);
        _idleActive.Remove(id);
        _foregroundActive.Remove(id);
        _foregroundGroups.Remove(id);
        if (_fileWatchers.Remove(id, out var watcher)) watcher.Dispose();
        _guard.Reset(id);
    }

    private IEnumerable<(EventRule Rule, T Trigger)> Rules<T>() where T : EventTrigger =>
        _rules.Values.Select(r => r.Rule).Where(r => r.Trigger is T).Select(r => (r, (T)r.Trigger));

    // ── running ──────────────────────────────────────────────────────────

    /// <summary>«Probar»: runs the commands now, as if by hand (no conditions, works while paused).</summary>
    public Task TestAsync(EventRule rule, bool end = false) => _engine.RunEventAsync(rule, end, TriggerSource.Manual);

    /// <summary>Runs an event now from a command, the command line…: no conditions, but not while paused.</summary>
    public Task RunNowAsync(EventRule rule, TriggerSource source) => _engine.RunEventAsync(rule, false, source);

    /// <summary>Stopped for a while because it fired too often (a loop).</summary>
    public bool IsSuspended(EventRule rule) => _guard.IsSuspended(rule.Id, DateTime.UtcNow);

    private void Fire(EventRule rule, bool end) => _ = FireAsync(rule, end);

    private Task FireAsync(EventRule rule, bool end) => Task.Run(async () =>
    {
        try { await FireCoreAsync(rule, end).ConfigureAwait(false); }
        catch (Exception ex) { Log.Error($"Error en el evento «{rule.Name}»", ex); }
    });

    private async Task FireCoreAsync(EventRule rule, bool end)
    {
        if (!rule.Enabled || _disposed) return;
        if (end)
        {
            lock (_lock)
            {
                if (!_started.Remove(rule.Id)) return; // its start didn't run (conditions, pause…)
            }
            if (rule.EndCommands.Count > 0) await _engine.RunEventAsync(rule, true).ConfigureAwait(false);
            return;
        }
        if (_engine.IsPaused)
        {
            Log.Info($"En pausa: no se ejecuta el evento «{rule.Name}»");
            return;
        }
        HashSet<string>? running = null;
        if (!rule.Conditions.Allows(DateTime.Now, name => (running ??= ProcessSnapshot.Names()).Contains(name), out var reason))
        {
            Log.Info($"Evento «{rule.Name}»: no se ejecuta ({reason})");
            return;
        }
        switch (_guard.TryFire(rule.Id, rule.CooldownSeconds, DateTime.UtcNow))
        {
            case EventGuard.Verdict.Cooldown:
                Log.Info($"Evento «{rule.Name}»: no se repite antes de {rule.CooldownSeconds} s");
                return;
            case EventGuard.Verdict.Suspended:
                Log.Info($"Evento «{rule.Name}»: detenido un rato por dispararse demasiadas veces");
                return;
            case EventGuard.Verdict.Loop:
                var minutes = (int)EventGuard.Pause.TotalMinutes;
                Log.Warn($"Evento «{rule.Name}»: más de {EventGuard.MaxPerMinute} veces en un minuto; se detiene {minutes} minutos");
                _ui.ShowOsd(Loc.T("event.loopStopped", rule.Name, minutes), OsdKind.Warning, TimeSpan.FromSeconds(5));
                return;
        }
        if (rule.HasEnd)
            lock (_lock) _started.Add(rule.Id);
        await _engine.RunEventAsync(rule, false).ConfigureAwait(false);
    }

    /// <summary>Remembers when an event fired (shown in the list, and «a una hora» doesn't repeat a time already done).</summary>
    private void OnEventFired(EventRule rule, bool end, TriggerSource source)
    {
        if (end || source == TriggerSource.Manual) return;
        var now = DateTime.UtcNow;
        _state.SetFired(rule.Id, now);
        _ui.RunOnUi(() => rule.LastFired = now.ToLocalTime());
    }

    // ── «A una hora» / «Cada N minutos» ───────────────────────────────────

    private void ConfigureSchedule()
    {
        var now = DateTime.UtcNow;
        var wanted = _rules.Values.Select(r => r.Rule).Where(r => r.Trigger is TimeTrigger or IntervalTrigger).ToList();
        foreach (var id in _due.Keys.Where(id => wanted.All(r => r.Id != id)).ToList()) _due.Remove(id);
        foreach (var rule in wanted)
            if (!_due.ContainsKey(rule.Id)) _due[rule.Id] = FirstDue(rule.Trigger, now);

        if (_initial) CatchUpMissed(now);

        if (wanted.Count == 0)
        {
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;
            return;
        }
        if (_lastHeartbeat == default && NeedsHeartbeat()) Beat(now);
        _scheduleTimer ??= new Timer(_ => OnScheduleTick(), null, Timeout.Infinite, Timeout.Infinite);
        ArmSchedule(now);
    }

    private static DateTime FirstDue(EventTrigger trigger, DateTime nowUtc) => trigger switch
    {
        TimeTrigger t when EventTime.Parse(t.Time) is { } time => EventSchedule.Next(time, t.Days, nowUtc),
        IntervalTrigger i => nowUtc.AddMinutes(Math.Max(1, i.Minutes)),
        _ => DateTime.MaxValue,
    };

    /// <summary>At start: «si se perdió, ejecutar al arrancar» for the times that passed while K-Desktop was closed.</summary>
    private void CatchUpMissed(DateTime nowUtc)
    {
        if (_state.LastSeen is not { } seen) return;
        foreach (var (rule, t) in Rules<TimeTrigger>())
        {
            if (!t.RunIfMissed || EventTime.Parse(t.Time) is not { } time) continue;
            var from = _state.LastFired(rule.Id) is { } fired && fired > seen ? fired : seen;
            if (EventSchedule.LastBetween(time, t.Days, from, nowUtc) is not { } missed) continue;
            Log.Info($"Evento «{rule.Name}»: se perdió la hora de las {missed.ToLocalTime():HH:mm} del {missed.ToLocalTime():d}; se ejecuta ahora");
            Fire(rule, false);
        }
    }

    private bool NeedsHeartbeat() => Rules<TimeTrigger>().Any(r => r.Trigger.RunIfMissed);

    private void Beat(DateTime nowUtc)
    {
        _lastHeartbeat = nowUtc;
        _state.Seen(nowUtc, save: true);
    }

    private void ArmSchedule(DateTime nowUtc)
    {
        if (_scheduleTimer is null) return;
        var next = _due.Count == 0 ? DateTime.MaxValue : _due.Values.Min();
        var wait = next == DateTime.MaxValue ? MaxScheduleWait : next - nowUtc;
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        if (wait > MaxScheduleWait) wait = MaxScheduleWait; // re-checked often: sleep and clock changes are noticed
        _scheduleTimer.Change(wait, Timeout.InfiniteTimeSpan);
    }

    private void OnScheduleTick()
    {
        var fire = new List<EventRule>();
        lock (_lock)
        {
            if (_disposed || _scheduleTimer is null) return;
            var now = DateTime.UtcNow;
            if (_lastTick != default && now < _lastTick - TimeSpan.FromMinutes(1))
            {
                // The clock went back: plan everything again from the new time.
                Log.Info("Eventos: el reloj ha retrocedido; se recalculan las horas");
                foreach (var id in _due.Keys.ToList()) _due[id] = FirstDue(_rules[id].Rule.Trigger, now);
            }
            _lastTick = now;

            foreach (var (id, due) in _due.ToList())
            {
                if (due > now || !_rules.TryGetValue(id, out var entry)) continue;
                var rule = entry.Rule;
                var late = now - due > LateLimit;
                if (rule.Trigger is TimeTrigger t && late && !t.RunIfMissed)
                    Log.Info($"Evento «{rule.Name}»: se pasó la hora ({due.ToLocalTime():HH:mm}) con el PC dormido o el reloj cambiado");
                else
                    fire.Add(rule);
                _due[id] = rule.Trigger is IntervalTrigger i ? now.AddMinutes(Math.Max(1, i.Minutes)) : FirstDue(rule.Trigger, now);
            }
            if (NeedsHeartbeat() && now - _lastHeartbeat > Heartbeat) Beat(now);
            ArmSchedule(now);
        }
        foreach (var rule in fire) Fire(rule, false);
    }

    // ── «Al abrir un programa» ───────────────────────────────────────────

    private void ConfigureProcesses()
    {
        var groups = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (rule, t) in Rules<ProcessStartTrigger>())
        {
            var names = Normalize(t.Processes);
            if (names.Length > 0) groups[rule.Id] = names;
        }
        if (groups.Count == 0)
        {
            _processTimer?.Dispose();
            _processTimer = null;
            _processes.SetGroups(groups, []);
            return;
        }
        var running = ProcessSnapshot.Names();
        _processes.SetGroups(groups, running);
        if (_initial && !_afterUpdate)
        {
            foreach (var (rule, t) in Rules<ProcessStartTrigger>())
                if (t.IncludeRunningAtStart && _processes.IsOpen(rule.Id))
                {
                    Log.Info($"Evento «{rule.Name}»: {string.Join(", ", groups[rule.Id])} ya estaba abierto al iniciar K-Desktop");
                    Fire(rule, false);
                }
        }
        _processTimer ??= new Timer(_ => OnProcessTick(), null, ProcessPoll, ProcessPoll);
    }

    private void OnProcessTick()
    {
        List<(EventRule Rule, bool Opened)> changes = [];
        try
        {
            var running = ProcessSnapshot.Names();
            lock (_lock)
            {
                if (_disposed || _processTimer is null) return;
                foreach (var (id, opened) in _processes.Update(running))
                    if (_rules.TryGetValue(id, out var entry)) changes.Add((entry.Rule, opened));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Eventos: error mirando los procesos", ex);
            return;
        }
        foreach (var (rule, opened) in changes)
        {
            var names = rule.Trigger is ProcessStartTrigger t ? string.Join(", ", Normalize(t.Processes)) : "?";
            Log.Info($"Eventos: {(opened ? "se ha abierto" : "se ha cerrado")} {names} («{rule.Name}»)");
            Fire(rule, !opened);
        }
    }

    // ── «En primer plano» ────────────────────────────────────────────────

    private void ConfigureForeground()
    {
        var current = (string?)null;
        foreach (var id in _foregroundGroups.Keys.ToList())
            if (!_rules.TryGetValue(id, out var r) || r.Rule.Trigger is not ForegroundTrigger)
            {
                _foregroundGroups.Remove(id);
                _foregroundActive.Remove(id);
            }
        foreach (var (rule, t) in Rules<ForegroundTrigger>())
        {
            var names = Normalize(t.Processes);
            if (names.Length == 0) continue;
            if (_foregroundGroups.ContainsKey(rule.Id)) continue;
            _foregroundGroups[rule.Id] = names;
            current ??= ForegroundWindow.Capture().ProcessName;
            _foregroundActive[rule.Id] = names.Contains(current); // already in front: not «coming to the front»
        }
        if (_foregroundGroups.Count == 0)
        {
            _foregroundTimer?.Dispose();
            _foregroundTimer = null;
        }
        else _foregroundTimer ??= new Timer(_ => OnForegroundSettled(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Alt+Tab goes through several windows: wait until one stays in front.</summary>
    private void OnForegroundChanged(IntPtr hwnd)
    {
        lock (_lock) _foregroundTimer?.Change(ForegroundSettleMs, Timeout.Infinite);
    }

    private void OnForegroundSettled()
    {
        var process = ForegroundWindow.Capture().ProcessName;
        List<(EventRule Rule, bool Active)> changes = [];
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var (id, names) in _foregroundGroups)
            {
                var active = names.Contains(process);
                if (_foregroundActive.GetValueOrDefault(id) == active || !_rules.TryGetValue(id, out var entry)) continue;
                _foregroundActive[id] = active;
                changes.Add((entry.Rule, active));
            }
        }
        foreach (var (rule, active) in changes)
        {
            Log.Info($"Eventos: {(active ? $"{process} pasa a primer plano" : "deja de estar en primer plano")} («{rule.Name}»)");
            Fire(rule, !active);
        }
    }

    // ── «Sin usar el PC» ─────────────────────────────────────────────────

    private void ConfigureIdle()
    {
        foreach (var id in _idleActive.Keys.Where(id => !_rules.TryGetValue(id, out var r) || r.Rule.Trigger is not IdleTrigger).ToList())
            _idleActive.Remove(id);
        var any = false;
        foreach (var (rule, _) in Rules<IdleTrigger>())
        {
            any = true;
            _idleActive.TryAdd(rule.Id, false);
        }
        if (!any)
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
            return;
        }
        if (_idleTimer is null)
        {
            _idleFast = false;
            _idleTimer = new Timer(_ => OnIdleTick(), null, IdlePoll, IdlePoll);
        }
    }

    private void OnIdleTick()
    {
        var idle = ProcessSnapshot.IdleTime();
        List<(EventRule Rule, bool Active)> changes = [];
        lock (_lock)
        {
            if (_disposed || _idleTimer is null) return;
            foreach (var (rule, t) in Rules<IdleTrigger>())
            {
                var active = idle >= TimeSpan.FromMinutes(Math.Max(1, t.Minutes));
                if (_idleActive.GetValueOrDefault(rule.Id) == active) continue;
                _idleActive[rule.Id] = active;
                changes.Add((rule, active));
            }
            // While someone is away, look more often so «al volver» is quick.
            var fast = _idleActive.Values.Any(a => a);
            if (fast != _idleFast)
            {
                _idleFast = fast;
                var period = fast ? IdlePollActive : IdlePoll;
                _idleTimer.Change(period, period);
            }
        }
        foreach (var (rule, active) in changes)
        {
            Log.Info(active
                ? $"Eventos: el PC lleva {(int)idle.TotalMinutes} min sin usarse («{rule.Name}»)"
                : $"Eventos: se vuelve a usar el PC («{rule.Name}»)");
            Fire(rule, !active);
        }
    }

    // ── «Archivo nuevo en una carpeta» ───────────────────────────────────

    private void ConfigureFiles()
    {
        foreach (var id in _fileWatchers.Keys.Where(id => !_rules.TryGetValue(id, out var r) || r.Rule.Trigger is not FileTrigger).ToList())
            if (_fileWatchers.Remove(id, out var old)) old.Dispose();
        foreach (var (rule, t) in Rules<FileTrigger>())
        {
            if (_fileWatchers.ContainsKey(rule.Id)) continue;
            var folder = Environment.ExpandEnvironmentVariables(t.Folder.Trim().Trim('"'));
            if (folder.Length == 0) continue;
            if (!Directory.Exists(folder))
            {
                Log.Warn($"Evento «{rule.Name}»: la carpeta no existe ({folder})");
                continue;
            }
            var filter = string.IsNullOrWhiteSpace(t.Filter) ? "*" : t.Filter.Trim();
            try
            {
                var watcher = new FileSystemWatcher(folder, filter)
                {
                    IncludeSubdirectories = t.Subfolders,
                    NotifyFilter = NotifyFilters.FileName,
                    InternalBufferSize = 64 * 1024,
                };
                var captured = rule;
                watcher.Created += (_, e) => OnFileAppeared(captured, filter, e.FullPath);
                watcher.Renamed += (_, e) => OnFileAppeared(captured, filter, e.FullPath);
                watcher.Error += (_, e) => Log.Warn($"Evento «{captured.Name}»: se perdieron avisos de la carpeta ({e.GetException().Message})");
                watcher.EnableRaisingEvents = true;
                _fileWatchers[rule.Id] = watcher;
            }
            catch (Exception ex)
            {
                Log.Warn($"Evento «{rule.Name}»: no se puede vigilar {folder}: {ex.Message}");
            }
        }
    }

    /// <summary>A file appeared (created, or renamed to a matching name): wait until it is complete, then fire.</summary>
    private void OnFileAppeared(EventRule rule, string filter, string path)
    {
        if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(filter, Path.GetFileName(path))) return;
        if (Directory.Exists(path)) return; // a folder, not a file
        lock (_lock)
        {
            if (_disposed || !_settling.Add(rule.Id + "|" + path)) return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (await FileSettler.WaitAsync(path, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), CancellationToken.None).ConfigureAwait(false))
                {
                    Log.Info($"Eventos: archivo nuevo {path} («{rule.Name}»)");
                    await FireAsync(rule, false).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Log.Error($"Evento «{rule.Name}»: error esperando a {path}", ex); }
            finally
            {
                lock (_lock) _settling.Remove(rule.Id + "|" + path);
            }
        });
    }

    // ── sesión y ventana oculta ──────────────────────────────────────────

    private void ConfigureWatcher()
    {
        var foreground = _foregroundGroups.Count > 0;
        var session = Rules<SessionTrigger>().Any();
        var power = Rules<PowerSourceTrigger>().Any();
        if (!foreground && !session && !power)
        {
            _watcher?.Dispose();
            _watcher = null;
            return;
        }
        if (_watcher is null)
        {
            _watcher = new SystemWatcher();
            _watcher.ForegroundChanged += OnForegroundChanged;
            _watcher.SessionChanged = OnSession;
            _watcher.PowerSourceChanged += OnPowerSource;
        }
        _watcher.WatchForeground(foreground);
        _watcher.WatchPower(power);
    }

    private Task? OnSession(SessionEvent change)
    {
        List<EventRule> rules;
        lock (_lock) rules = Rules<SessionTrigger>().Where(r => r.Trigger.Event == change).Select(r => r.Rule).ToList();
        if (rules.Count == 0) return null;
        Log.Info($"Eventos: {change switch
        {
            SessionEvent.Lock => "sesión bloqueada",
            SessionEvent.Unlock => "sesión desbloqueada",
            SessionEvent.Suspend => "el PC se suspende",
            _ => "el PC se reanuda",
        }}");
        return Task.WhenAll(rules.Select(r => FireAsync(r, false)));
    }

    private void OnPowerSource(bool battery)
    {
        var source = battery ? PowerSource.Battery : PowerSource.Mains;
        List<EventRule> rules;
        lock (_lock) rules = Rules<PowerSourceTrigger>().Where(r => r.Trigger.Source == source).Select(r => r.Rule).ToList();
        Log.Info($"Eventos: el PC pasa a {(battery ? "batería" : "corriente")}");
        foreach (var rule in rules) Fire(rule, false);
    }

    private static string[] Normalize(IEnumerable<string> processes) =>
        processes.ToArray().Select(ProcessNames.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _scheduleTimer?.Dispose();
            _processTimer?.Dispose();
            _foregroundTimer?.Dispose();
            _idleTimer?.Dispose();
            foreach (var w in _fileWatchers.Values) w.Dispose();
            _fileWatchers.Clear();
        }
        _watcher?.Dispose();
        _engine.EventFired -= OnEventFired;
        if (NeedsHeartbeat()) _state.Seen(DateTime.UtcNow, save: false);
        _state.Dispose();
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"EventService({_rules.Count} eventos)");
}
