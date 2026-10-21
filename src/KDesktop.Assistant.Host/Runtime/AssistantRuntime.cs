using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using KDesktop.Assistant.Host.Audio;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// Runs the assistants on the desktop on the engine thread (stage 1.11: several at once): owns the overlay and the
/// world they share, ticks each one at the pace it asks for (in step with the compositor while it moves), and contains
/// their failures one by one: an exception is logged, and several in a row unload that assistant only. Without
/// assistants it costs nothing.
/// </summary>
internal sealed class AssistantRuntime : IDisposable
{
    /// <summary>Failures in a row that unload an assistant.</summary>
    private const int MaxFaults = 5;

    private readonly EngineLoop _engine;
    private readonly KDesktopLink? _link;
    private readonly PanelPublisher _panel;
    /// <summary>On the desktop, in the order they were loaded (the order they tick in).</summary>
    private readonly List<AssistantSlot> _slots = [];
    /// <summary>The hotkeys of each assistant's extras, as K-Desktop last sent them (by id: they may come before its load).</summary>
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _extraKeys = new(StringComparer.Ordinal);
    private double _nextOnTop;

    /// <summary>The language each assistant speaks in (its own, or the process's): what its <c>host.Language</c> says.</summary>
    public Func<string, string>? LanguageOf { get; set; }

    public AssistantRuntime(EngineLoop engine, KDesktopLink? link)
    {
        _engine = engine;
        _link = link;
        _panel = new PanelPublisher(message => _link?.Send(message));
        World = new HostWorld(() => engine.Now);
        World.Changed += snapshot => Each(slot => Guard(slot, _ => slot.World.Raise(snapshot), "World.Changed"));
        Watcher = new WorldWatcher(World, () => engine.Now, OwnWindows, e => Each(slot => Guard(slot, a => a.OnSystemEvent(e), "OnSystemEvent")), engine.RequestTick);
        WindowServices = new HostWindowServices(Watcher.Scanner, message => _link?.Send(message) == true);
    }

    public HostRendering Rendering { get; private set; } = null!;

    public HostWorld World { get; }

    /// <summary>Watches the desktop once an assistant is loaded (with none, nothing is watched).</summary>
    public WorldWatcher Watcher { get; }

    public IWindowServices WindowServices { get; }

    /// <summary>The sound of every assistant (one mixer, one output that only exists while something sounds).</summary>
    public HostAudio Audio { get; } = new();

    /// <summary>The ids of the assistants on the desktop, in the order they were loaded.</summary>
    public IReadOnlyList<string> Loaded => [.. _slots.Select(s => s.Id)];

    public bool IsLoaded(string id) => Find(id) is not null;

    /// <summary>Whether that assistant is on the desktop now, from any thread (a copy kept as they come and go).</summary>
    public bool HasBody(string id) => _bodies.Contains(id);

    private volatile HashSet<string> _bodies = new(StringComparer.Ordinal);

    private void BodiesChanged() => _bodies = new HashSet<string>(_slots.Where(s => !s.Dropped).Select(s => s.Id), StringComparer.Ordinal);

    /// <summary>An assistant was unloaded because it kept failing (its id).</summary>
    public event Action<string>? Faulted;

    /// <summary>The right button on an assistant: show the host's menu. Engine thread.</summary>
    public event Action<MenuRequest>? MenuRequested;

    /// <summary>On the engine thread, before the first tick.</summary>
    public void Start()
    {
        Rendering = new HostRendering();
        Faulted += id => _link?.Send(new LoadedMessage(id, false, "falla una y otra vez"));
        Watcher.MonitorsChanged += Rendering.UpdateBounds;
    }

    /// <summary>One engine tick: ticks the assistants that are due and returns the seconds it may sleep.</summary>
    public double Tick(double now)
    {
        Rendering.Pacer.Update(now);
        var interval = 60.0;
        if (Watcher.IsStarted)
        {
            interval = Watcher.Update(now);
            // A DirectX game or a presentation holds the screen: give the GPU back to it.
            if (Watcher.ExclusiveFullscreen) Rendering.Suspend();
            else Rendering.Resume();
        }
        // Half a frame early still counts: the tick is placed on the compositor's beat, not on the exact moment asked.
        var early = Rendering.FrameInterval / 2;
        foreach (var slot in _slots.ToList())
        {
            if (slot.Dropped) continue;
            if (slot.TickRequested || now >= slot.Due - early)
            {
                slot.TickRequested = false;
                var elapsed = Math.Clamp(now - slot.LastTick, 0, 0.25);
                var wanted = 1.0;
                if (!Guard(slot, a => wanted = a.Tick(new TickInfo(now, elapsed)), "Tick")) wanted = 1;
                slot.LastTick = now;
                slot.Due = now + (double.IsFinite(wanted) ? Math.Clamp(wanted, 0, 60) : 1);
            }
            if (!slot.Dropped) interval = Math.Min(interval, Math.Max(0, slot.Due - now));
        }
        if (now >= _nextOnTop)
        {
            // Other «always on top» windows may cover it: take the top back every few seconds, without fighting.
            _nextOnTop = now + 5;
            Rendering.KeepOnTop();
            foreach (var slot in _slots) slot.Pointer.KeepOnTop();
        }
        foreach (var slot in _slots)
            if (slot.Bubble is { } bubble) interval = Math.Min(interval, UpdateBubble(slot, bubble, now));
        Rendering.Commit();
        return interval;
    }

    /// <summary>A bubble follows its character; one that faded out goes.</summary>
    private double UpdateBubble(AssistantSlot slot, HostBubble bubble, double now)
    {
        if (bubble.IsOver(now) || slot.Dropped)
        {
            bubble.Dispose();
            slot.Bubble = null;
            return 60;
        }
        Point? anchor = null;
        Guard(slot, a => anchor = a.BubbleAnchor, "BubbleAnchor");
        var monitor = anchor is { } at ? World.Current.MonitorAt(at) ?? World.Current.PrimaryMonitor : null;
        return bubble.Update(now, anchor, monitor, Rendering.FrameInterval);
    }

    /// <summary>
    /// The moment of the next tick for an interval: frame-rate intervals fall right after a composition (smooth
    /// movement); longer ones need no alignment.
    /// </summary>
    public double NextTick(double now, double interval) =>
        interval <= Rendering.FrameInterval * 1.5 ? Rendering.Pacer.Align(now + interval) : now + interval;

    /// <summary>
    /// Loads an assistant package from its folder next to the others. One with the same id already on the desktop (an
    /// older version) leaves first, once the new code has loaded. Returns its id.
    /// </summary>
    public async Task<string> LoadPackageAsync(string folder, string? data, EntranceKind entrance)
    {
        var package = PackageLoader.Load(folder, data);
        await LoadAsync(package.Assistant, package.Manifest.Id, package.Folder, package.DataFolder, package.Settings, entrance,
            package.Manifest, package.Context).ConfigureAwait(true);
        return package.Manifest.Id;
    }

    /// <summary>Loads an assistant next to the others (one with its id leaves first, with its exit).</summary>
    public async Task LoadAsync(IAssistant assistant, string id, string package, string data, JsonElement settings, EntranceKind entrance,
        AssistantManifest? manifest = null, AssemblyLoadContext? context = null)
    {
        if (Find(id) is not null) await UnloadAsync(id, ExitKind.Switching).ConfigureAwait(true);
        if (!Watcher.IsStarted) Watcher.Start(_engine.Now, _engine.Post);
        var slot = new AssistantSlot(id, assistant, manifest, context, Rendering, World) { LastTick = _engine.Now };
        slot.Pointer = new HostPointer(e => Guard(slot, a => a.OnPointer(e), "OnPointer"), (area, at) => OnMenu(slot, at), UpdateWheel, _ => OnWrite(slot),
            (_, active) => OnTalk(slot, active));
        slot.Keyboard = new HostKeyboard(keys => _link?.Send(new KeysMessage(id, [.. keys.Select(k => k.ToString())])));
        slot.Audio = new AssistantAudio(Audio, package);
        slot.Host = new AssistantHost(this, slot, package, data, settings);
        _slots.Add(slot);
        BodiesChanged();
        if (!Guard(slot, a => a.Attach(slot.Host), "Attach") || !Guard(slot, a => a.Enter(entrance), "Enter"))
        {
            // Half attached it would only fail again on every tick: it does not stay.
            Drop(slot);
            throw new InvalidOperationException($"«{id}» ha fallado al entrar en el escritorio (detalles en assistant.log)");
        }
        Log.Info($"Asistente: «{id}» cargado ({_slots.Count} en el escritorio)");
        RequestTick(slot);
    }

    /// <summary>Lets an assistant leave (with a time limit) and drops it; the others go on.</summary>
    public async Task UnloadAsync(string id, ExitKind why)
    {
        if (Find(id) is not { } slot) return;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await slot.Assistant.ExitAsync(why, limit.Token).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Asistente: «{id}» no ha salido bien ({ex.Message})");
        }
        Drop(slot);
        Log.Info($"Asistente: «{id}» descargado ({why})");
    }

    /// <summary>All of them leave at once (each with its time limit).</summary>
    public Task UnloadAllAsync(ExitKind why) => Task.WhenAll(_slots.Select(s => s.Id).ToList().Select(id => UnloadAsync(id, why)).ToList());

    /// <summary>The settings of an assistant on the desktop changed in K-Desktop's Options.</summary>
    public void ApplySettings(string id, JsonElement settings)
    {
        if (Find(id) is not { } slot) return;
        slot.Host.Settings = settings;
        Guard(slot, a => a.OnSettingsChanged(settings), "OnSettingsChanged");
    }

    /// <summary>Runs one of an assistant's extras (from its menu); it is cancelled if the assistant leaves.</summary>
    public async Task RunExtraAsync(string id, string extraId)
    {
        if (Find(id) is not { } slot) return;
        Task? running = null;
        var token = slot.Extras.Token;
        if (!Guard(slot, a => running = a.OnExtraAsync(extraId, token), "OnExtraAsync") || running is null) return;
        try { await running.ConfigureAwait(true); }
        catch (OperationCanceledException) { /* it left */ }
        catch (Exception ex) { Log.Error($"Asistente: el extra «{extraId}» de «{id}» ha fallado", ex); }
    }

    /// <summary>K-Desktop's <c>key</c>: a key it captures for that assistant went down or up.</summary>
    public void OnKey(string id, string key, bool down)
    {
        if (Find(id) is not { } slot || !Enum.TryParse<System.Windows.Input.Key>(key, out var parsed)) return;
        Guard(slot, _ => slot.Keyboard.OnKey(parsed, down), "KeyChanged");
    }

    /// <summary>K-Desktop's <c>wheel</c>: to those watching it.</summary>
    public void OnWheel(WheelMessage wheel)
    {
        var e = new SystemEvent.WheelTurned(new Point(wheel.X, wheel.Y), wheel.Delta / 120.0, wheel.Horizontal, wheel.Hwnd);
        Each(slot =>
        {
            if (slot.Pointer.WatchWheel) Guard(slot, a => a.OnSystemEvent(e), "OnSystemEvent");
        });
    }

    /// <summary>K-Desktop's <c>fastScroll</c>: to everybody.</summary>
    public void OnFastScroll(FastScrollMessage scroll)
    {
        var e = new SystemEvent.FastScroll(Math.Clamp(scroll.Strength, -1, 1), new Point(scroll.X, scroll.Y));
        Each(slot => Guard(slot, a => a.OnSystemEvent(e), "OnSystemEvent"));
    }

    /// <summary>Asks K-Desktop for every turn of the wheel while some assistant watches it.</summary>
    private void UpdateWheel()
    {
        var watch = _slots.Any(s => !s.Dropped && s.Pointer?.WatchWheel == true);
        if (watch == _wheelWatched) return;
        _wheelWatched = watch;
        _link?.Send(new WatchWheelMessage(watch));
    }

    private bool _wheelWatched;

    /// <summary>K-Desktop was paused or resumed.</summary>
    public void SetPaused(bool paused) => Watcher.SetPaused(paused);

    public void PublishPanel(string assistant, JsonObject data) => _panel.Publish(assistant, data);

    public void Notify(string assistant, string title, string text) => _link?.Send(new NotifyMessage(title, text, assistant));

    /// <summary>What the module is doing for an assistant (thinking, speaking…), for the character to show it.</summary>
    public void SetState(string id, AssistantState state)
    {
        if (Find(id) is { } slot) Guard(slot, a => a.SetState(state), "SetState");
    }

    /// <summary>
    /// The character «says» <paramref name="text"/> (Communicate) for <paramref name="duration"/>, or less if cancelled or it
    /// leaves. One that cannot do it now (asleep in its game…) just lets it pass. Engine thread.
    /// </summary>
    public async Task CommunicateAsync(string id, string text, TimeSpan duration, CancellationToken cancellationToken, Task? speaking = null)
    {
        if (Find(id) is not { } slot) return;
        // The bubble is the host's (it says it whatever the gesture): over the character, for as long as it takes to read.
        slot.Bubble?.Dispose();
        var bubble = slot.Bubble = new HostBubble(Rendering.CreateSurface(SurfaceLayer.Front), text, _link?.Theme == "dark", _engine.Now, duration.TotalSeconds);
        _engine.RequestTick();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, slot.Extras.Token);
        limit.CancelAfter(duration);
        try
        {
            await GestureAsync(slot, new Gesture.Communicate(text, null), duration, limit.Token).ConfigureAwait(true);
            // Its voice (stage 5.4) may take longer than reading: the bubble stays until it has said it (a minute at most).
            var until = _engine.Now + 60;
            while (speaking is { IsCompleted: false } && !cancellationToken.IsCancellationRequested && !slot.Extras.IsCancellationRequested && _engine.Now < until)
            {
                bubble.KeepUntil(_engine.Now + 0.6);
                await Task.WhenAny(speaking, Task.Delay(250, CancellationToken.None)).ConfigureAwait(true);
            }
        }
        finally
        {
            bubble.Close(_engine.Now);
            _engine.RequestTick();
        }
    }

    /// <summary>How loud its voice is now (0…1, about 30 times a second while it speaks): lights or a mouth follow it.</summary>
    public void VoiceLevel(string id, double level)
    {
        if (Find(id) is { } slot) Guard(slot, a => a.OnVoiceLevel(level), "OnVoiceLevel");
    }

    /// <summary>
    /// The moods its next sentence is said in: the one it set (<c>SetVoiceMood</c>). Wherever it is, it sounds as if it
    /// were on the monitor in use (the automatic «far» of 5.5 is gone, at the user's request). Engine thread.
    /// </summary>
    public IReadOnlyList<string> VoiceMoods(string id) =>
        Find(id)?.VoiceMood is { } mood ? [mood] : [];

    /// <summary>
    /// Where its voice sounds, by where it is across all the monitors (−1 left … 1 right, never all to one side). Engine
    /// thread.
    /// </summary>
    public double PanOf(string id)
    {
        if (Find(id) is not { } slot) return 0;
        Point? anchor = null;
        Guard(slot, a => anchor = a.BubbleAnchor, "BubbleAnchor");
        var monitors = World.Current.Monitors;
        if (anchor is not { } at || monitors.Count == 0) return 0;
        var left = monitors.Min(m => m.Bounds.Left);
        var right = monitors.Max(m => m.Bounds.Right);
        return right > left ? Math.Clamp((at.X - left) / (right - left) * 2 - 1, -1, 1) * 0.6 : 0;
    }

    /// <summary>
    /// Communicate for the module: the character does its gesture while the bubble shows (one that cannot now just lets
    /// the bubble talk until the time is up).
    /// </summary>
    private async Task GestureAsync(AssistantSlot slot, Gesture.Communicate gesture, TimeSpan duration, CancellationToken token)
    {
        var id = slot.Id;
        var can = false;
        Task<GestureOutcome>? running = null;
        var context = new SpeechGestureContext(_engine, duration);
        if (!Guard(slot, a => can = a.CanPerform(gesture), "CanPerform") || !can
            || !Guard(slot, a => running = a.PerformAsync(gesture, context, token), "PerformAsync") || running is null)
        {
            try { await Task.Delay(duration, token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            return;
        }
        try
        {
            // Its time is up when the token fires; a little more for it to finish, never forever.
            await running.WaitAsync(duration + TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            Log.Warn($"Asistente: «{id}» no termina el gesto Communicate");
        }
        catch (OperationCanceledException)
        {
            // Cancelled: the bubble is over.
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: «{id}» ha fallado en PerformAsync", ex);
        }
    }

    /// <summary>
    /// An assistant shows, greys out or hides one of its extras (<see cref="IAssistantHost.SetExtraState"/>): kept for the
    /// host's own menu, and sent to K-Desktop when it changes.
    /// </summary>
    public void SetExtraState(AssistantSlot slot, string extra, ExtraState state)
    {
        if (slot.Dropped || string.IsNullOrEmpty(extra) || slot.ExtraStates.GetValueOrDefault(extra, ExtraState.Shown) == state) return;
        if (state == ExtraState.Shown) slot.ExtraStates.Remove(extra);
        else slot.ExtraStates[extra] = state;
        _link?.Send(new ExtraStateMessage(slot.Id, extra, state switch
        {
            ExtraState.Disabled => "disabled",
            ExtraState.Hidden => "hidden",
            _ => "shown",
        }));
    }

    /// <summary>K-Desktop's <c>extraKeys</c>: the hotkeys of the commands that run an assistant's extras.</summary>
    public void SetExtraKeys(string assistant, IReadOnlyDictionary<string, string> keys) => _extraKeys[assistant] = keys;

    /// <summary>The hotkey of the command that runs an extra («CTRL + ALT + L»), or null.</summary>
    public string? ExtraHotkey(string assistant, string extra) =>
        _extraKeys.TryGetValue(assistant, out var keys) && keys.TryGetValue(extra, out var key) && key.Length > 0 ? key : null;

    /// <summary>A tick for this assistant as soon as possible (any thread).</summary>
    public void RequestTick(AssistantSlot slot)
    {
        slot.TickRequested = true;
        _engine.RequestTick();
    }

    public void Post(Action action) => _engine.Post(action);

    public void Dispose()
    {
        foreach (var slot in _slots.ToList()) Drop(slot);
        Watcher.Dispose();
        Rendering?.Dispose();
        Audio.Dispose();
    }

    /// <summary>
    /// A gesture of a command's step (stage 6.4), if that assistant can do it now: its task, or null when it cannot (or
    /// failed to start it). Engine thread.
    /// </summary>
    public Task<GestureOutcome>? TryPerform(string id, Gesture gesture, IGestureContext context, CancellationToken token)
    {
        if (Find(id) is not { } slot) return null;
        var can = false;
        Task<GestureOutcome>? running = null;
        if (!Guard(slot, a => can = a.CanPerform(gesture), "CanPerform") || !can) return null;
        return Guard(slot, a => running = a.PerformAsync(gesture, context, token), "PerformAsync " + gesture.Kind) ? running : null;
    }

    private AssistantSlot? Find(string id) => _slots.FirstOrDefault(s => s.Id == id);

    /// <summary>Something for every assistant on the desktop (one that fails and is dropped meanwhile is skipped).</summary>
    private void Each(Action<AssistantSlot> action)
    {
        foreach (var slot in _slots.ToList())
            if (!slot.Dropped) action(slot);
    }

    /// <summary>Calls an assistant; false when it threw (counted; several in a row unload it).</summary>
    private bool Guard(AssistantSlot slot, Action<IAssistant> call, string what)
    {
        if (slot.Dropped) return false;
        try
        {
            call(slot.Assistant);
            slot.Faults = 0;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: «{slot.Id}» ha fallado en {what}", ex);
            if (++slot.Faults >= MaxFaults)
            {
                Log.Warn($"Asistente: «{slot.Id}» falla una y otra vez; se descarga");
                Drop(slot);
                Faulted?.Invoke(slot.Id);
            }
            return false;
        }
    }

    private void Drop(AssistantSlot slot)
    {
        if (slot.Dropped) return;
        slot.Dropped = true;
        _slots.Remove(slot);
        BodiesChanged();
        try { slot.Assistant.Dispose(); }
        catch (Exception ex) { Log.Error($"Asistente: error al liberar «{slot.Id}»", ex); }
        slot.Extras.Cancel();
        slot.Extras.Dispose();
        // What it left behind goes with it: its surfaces, its hit areas and its subscriptions to the world.
        slot.Bubble?.Dispose();
        slot.Bubble = null;
        slot.Surfaces.DisposeAll();
        slot.Pointer.Dispose();
        slot.World.Forget();
        slot.Audio.DisposeAll();
        if (slot.Keyboard.IsCapturing) slot.Keyboard.Release();
        UpdateWheel();
        if (slot.Release() is { } context)
        {
            var id = slot.Id;
            // Later, from the engine queue: now the caller's frames (async ones too) still hold the assistant.
            _engine.Post(() => PackageLoader.Unload(context, id));
        }
    }

    private IEnumerable<nint> OwnWindows()
    {
        var handles = new List<nint>();
        if (Rendering is not null) handles.Add(Rendering.OverlayHandle);
        foreach (var slot in _slots) handles.AddRange(slot.Pointer.Handles);
        return handles;
    }

    /// <summary>
    /// A middle click on an assistant: K-Desktop opens the box to write to it above it (where its bubbles point), and
    /// takes the foreground for it.
    /// </summary>
    private void OnWrite(AssistantSlot slot)
    {
        if (slot.Dropped || _link is not { IsConnected: true } link) return;
        Point? anchor = null;
        Guard(slot, a => anchor = a.BubbleAnchor, "BubbleAnchor");
        var at = anchor ?? (Interop.Native.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : default);
        Interop.Native.AllowSetForegroundWindow(Interop.Native.ASFW_ANY);
        link.Send(new WriteMessage(slot.Id, (int)Math.Round(at.X), (int)Math.Round(at.Y)));
    }

    /// <summary>A long click on an assistant (stage 5.2): K-Desktop records the user while the button is held, for it.</summary>
    private void OnTalk(AssistantSlot slot, bool active)
    {
        if (slot.Dropped && active) return;
        _link?.Send(new TalkMessage(slot.Id, active));
    }

    /// <summary>The host's menu for the assistant clicked (panel, settings, extras, hide; talking and writing come with the brain).</summary>
    private void OnMenu(AssistantSlot slot, Point at)
    {
        if (slot.Dropped) return;
        var language = Loc.Instance.Language;
        var name = slot.Manifest?.Name ?? slot.Id;
        var extras = slot.Manifest?.Extras
            .Where(e => slot.ExtraStates.GetValueOrDefault(e.Id) != ExtraState.Hidden)
            .Select(e => new MenuExtra(e.Id, e.Name.In(language), slot.ExtraStates.GetValueOrDefault(e.Id) != ExtraState.Disabled))
            .ToList() ?? [];
        MenuRequested?.Invoke(new MenuRequest(slot.Id, name, extras, at));
    }
}

/// <summary>
/// One assistant on the desktop and what is only its own: its host, its surfaces, its hit areas, its subscriptions to
/// the world, its failures, its running extras and when it wants its next tick. Engine thread.
/// </summary>
internal sealed class AssistantSlot(string id, IAssistant assistant, AssistantManifest? manifest, AssemblyLoadContext? context, HostRendering rendering, HostWorld world)
{
    /// <summary>Its code's load context (null for the built-in probe), held while it runs (see <see cref="LoadedPackage"/>).</summary>
    private AssemblyLoadContext? _context = context;

    public string Id { get; } = id;

    public IAssistant Assistant { get; private set; } = assistant;

    public AssistantManifest? Manifest { get; } = manifest;

    public AssistantHost Host { get; set; } = null!;

    public SurfaceScope Surfaces { get; } = new(rendering);

    public WorldView World { get; } = new(world);

    public HostPointer Pointer { get; set; } = null!;

    public HostKeyboard Keyboard { get; set; } = null!;

    public AssistantAudio Audio { get; set; } = null!;

    public CancellationTokenSource Extras { get; } = new();

    /// <summary>The module's bubble over it while it says something (stage 3.6), or null.</summary>
    public HostBubble? Bubble { get; set; }

    /// <summary>Its extras that are not simply shown (<see cref="IAssistantHost.SetExtraState"/>).</summary>
    public Dictionary<string, ExtraState> ExtraStates { get; } = new(StringComparer.Ordinal);

    /// <summary>The situation its voice speaks in (<c>SetVoiceMood</c>; read by the voice from another thread).</summary>
    public string? VoiceMood { get => Volatile.Read(ref _voiceMood); set => Volatile.Write(ref _voiceMood, value); }

    private string? _voiceMood;

    public int Faults { get; set; }

    public bool Dropped { get; set; }

    public double LastTick { get; set; }

    /// <summary>When it asked to be ticked next (engine clock).</summary>
    public double Due { get; set; }

    /// <summary>It asked for a tick as soon as possible (<see cref="IAssistantHost.RequestTick"/>, any thread).</summary>
    public bool TickRequested { get => Volatile.Read(ref _tickRequested); set => Volatile.Write(ref _tickRequested, value); }

    private bool _tickRequested;

    /// <summary>
    /// Lets go of the instance, its host and its load context, so the context can be collected. Returns a weak
    /// reference to the context to unload it (null for the probe).
    /// </summary>
    public WeakReference? Release()
    {
        var context = _context is null ? null : new WeakReference(_context);
        _context = null;
        Assistant = null!;
        Host = null!;
        return context;
    }
}

/// <summary>
/// <see cref="IRendering"/> for one assistant: its surfaces go on the shared overlay, and are remembered so they go
/// when it is unloaded (even if it did not dispose them).
/// </summary>
internal sealed class SurfaceScope(HostRendering rendering) : IRendering
{
    private readonly List<HostSurface> _own = [];

    public bool HardwareAccelerated => rendering.HardwareAccelerated;

    public double FrameInterval => rendering.FrameInterval;

    public ISurface CreateSurface(SurfaceLayer layer = SurfaceLayer.Main)
    {
        _own.RemoveAll(s => s.IsDisposed);
        var surface = (HostSurface)rendering.CreateSurface(layer);
        _own.Add(surface);
        return surface;
    }

    public void DisposeAll()
    {
        foreach (var surface in _own) surface.Dispose();
        _own.Clear();
        rendering.Commit();
    }
}

/// <summary>
/// <see cref="IWorld"/> for one assistant: the shared picture of the desktop, with subscribers of its own (forgotten
/// when it is unloaded; an exception in one is that assistant's failure).
/// </summary>
internal sealed class WorldView(HostWorld world) : IWorld
{
    public WorldSnapshot Current => world.Current;

    public SystemState System => world.System;

    public event Action<WorldSnapshot>? Changed;

    public void WatchClosely(double seconds) => world.WatchClosely(seconds);

    internal void Raise(WorldSnapshot snapshot) => Changed?.Invoke(snapshot);

    internal void Forget() => Changed = null;
}
