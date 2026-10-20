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

    public AssistantRuntime(EngineLoop engine, KDesktopLink? link)
    {
        _engine = engine;
        _link = link;
        _panel = new PanelPublisher(message => _link?.Send(message));
        World = new HostWorld(() => engine.Now);
        World.Changed += snapshot => Each(slot => Guard(slot, _ => slot.World.Raise(snapshot), "World.Changed"));
        Watcher = new WorldWatcher(World, () => engine.Now, OwnWindows, e => Each(slot => Guard(slot, a => a.OnSystemEvent(e), "OnSystemEvent")), engine.RequestTick);
        WindowServices = new HostWindowServices(Watcher.Scanner);
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
        Rendering.Commit();
        return interval;
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
        AssistantManifest? manifest = null, WeakReference? context = null)
    {
        if (Find(id) is not null) await UnloadAsync(id, ExitKind.Switching).ConfigureAwait(true);
        if (!Watcher.IsStarted) Watcher.Start(_engine.Now, _engine.Post);
        var slot = new AssistantSlot(id, assistant, manifest, context, Rendering, World) { LastTick = _engine.Now };
        slot.Pointer = new HostPointer(e => Guard(slot, a => a.OnPointer(e), "OnPointer"), (area, at) => OnMenu(slot, at), UpdateWheel);
        slot.Keyboard = new HostKeyboard(keys => _link?.Send(new KeysMessage(id, [.. keys.Select(k => k.ToString())])));
        slot.Audio = new AssistantAudio(Audio, package);
        slot.Host = new AssistantHost(this, slot, package, data, settings);
        _slots.Add(slot);
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
        try { slot.Assistant.Dispose(); }
        catch (Exception ex) { Log.Error($"Asistente: error al liberar «{slot.Id}»", ex); }
        slot.Extras.Cancel();
        slot.Extras.Dispose();
        // What it left behind goes with it: its surfaces, its hit areas and its subscriptions to the world.
        slot.Surfaces.DisposeAll();
        slot.Pointer.Dispose();
        slot.World.Forget();
        slot.Audio.DisposeAll();
        if (slot.Keyboard.IsCapturing) slot.Keyboard.Release();
        UpdateWheel();
        if (slot.Context is { } context)
        {
            var id = slot.Id;
            slot.Release();
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
internal sealed class AssistantSlot(string id, IAssistant assistant, AssistantManifest? manifest, WeakReference? context, HostRendering rendering, HostWorld world)
{
    public string Id { get; } = id;

    public IAssistant Assistant { get; private set; } = assistant;

    public AssistantManifest? Manifest { get; } = manifest;

    /// <summary>Its code's load context (null for the built-in probe).</summary>
    public WeakReference? Context { get; } = context;

    public AssistantHost Host { get; set; } = null!;

    public SurfaceScope Surfaces { get; } = new(rendering);

    public WorldView World { get; } = new(world);

    public HostPointer Pointer { get; set; } = null!;

    public HostKeyboard Keyboard { get; set; } = null!;

    public AssistantAudio Audio { get; set; } = null!;

    public CancellationTokenSource Extras { get; } = new();

    /// <summary>Its extras that are not simply shown (<see cref="IAssistantHost.SetExtraState"/>).</summary>
    public Dictionary<string, ExtraState> ExtraStates { get; } = new(StringComparer.Ordinal);

    public int Faults { get; set; }

    public bool Dropped { get; set; }

    public double LastTick { get; set; }

    /// <summary>When it asked to be ticked next (engine clock).</summary>
    public double Due { get; set; }

    /// <summary>It asked for a tick as soon as possible (<see cref="IAssistantHost.RequestTick"/>, any thread).</summary>
    public bool TickRequested { get => Volatile.Read(ref _tickRequested); set => Volatile.Write(ref _tickRequested, value); }

    private bool _tickRequested;

    /// <summary>Lets go of the instance and its host, so its load context can be collected.</summary>
    public void Release()
    {
        Assistant = null!;
        Host = null!;
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
