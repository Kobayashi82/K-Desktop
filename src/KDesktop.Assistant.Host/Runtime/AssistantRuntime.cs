using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// Runs the loaded assistant on the engine thread: owns the overlay, the hit areas and the world, ticks the
/// assistant at the pace it asks for (in step with the compositor while it moves), and contains its failures:
/// an exception is logged, and several in a row unload it. Without an assistant it costs nothing.
/// </summary>
internal sealed class AssistantRuntime : IDisposable
{
    /// <summary>Failures in a row that unload an assistant.</summary>
    private const int MaxFaults = 5;

    private readonly EngineLoop _engine;
    private readonly KDesktopLink? _link;
    private double _lastTick;
    private double _nextOnTop;
    private int _faults;
    private WeakReference? _context;

    public AssistantRuntime(EngineLoop engine, KDesktopLink? link)
    {
        _engine = engine;
        _link = link;
        World = new HostWorld(() => engine.Now);
        Watcher = new WorldWatcher(World, () => engine.Now, OwnWindows, e => Guard(a => a.OnSystemEvent(e), "OnSystemEvent"), engine.RequestTick);
        WindowServices = new HostWindowServices(Watcher.Scanner);
    }

    public HostRendering Rendering { get; private set; } = null!;

    public HostPointer Pointer { get; private set; } = null!;

    public HostWorld World { get; }

    /// <summary>Watches the desktop once an assistant is loaded (with none, nothing is watched).</summary>
    public WorldWatcher Watcher { get; }

    public IWindowServices WindowServices { get; }

    public IAssistant? Current { get; private set; }

    public AssistantHost? CurrentHost { get; private set; }

    /// <summary>The assistant was unloaded because it kept failing (its id).</summary>
    public event Action<string>? Faulted;

    /// <summary>On the engine thread, before the first tick.</summary>
    public void Start()
    {
        Rendering = new HostRendering();
        Pointer = new HostPointer(e => Guard(a => a.OnPointer(e), "OnPointer"), OnMenu);
        Faulted += id => _link?.Send(new LoadedMessage(id, false, "falla una y otra vez"));
        Watcher.MonitorsChanged += Rendering.UpdateBounds;
        _lastTick = _engine.Now;
    }

    /// <summary>One engine tick: returns the seconds it may sleep.</summary>
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
        if (Current is { } assistant)
        {
            var elapsed = Math.Clamp(now - _lastTick, 0, 0.25);
            var wanted = 1.0;
            if (!Guard(a => wanted = a.Tick(new TickInfo(now, elapsed)), "Tick")) wanted = 1;
            interval = Math.Min(interval, wanted);
        }
        _lastTick = now;
        if (now >= _nextOnTop)
        {
            // Other «always on top» windows may cover it: take the top back every few seconds, without fighting.
            _nextOnTop = now + 5;
            Rendering.KeepOnTop();
            Pointer.KeepOnTop();
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

    /// <summary>Loads an assistant package from its folder (unloading the current one first). Returns its id.</summary>
    public async Task<string> LoadPackageAsync(string folder, string? data, EntranceKind entrance)
    {
        if (Current is not null) await UnloadAsync(ExitKind.Switching).ConfigureAwait(true);
        var package = PackageLoader.Load(folder, data);
        _context = package.Context;
        await LoadAsync(package.Assistant, package.Manifest.Id, package.Folder, package.DataFolder, package.Settings, entrance).ConfigureAwait(true);
        return package.Manifest.Id;
    }

    /// <summary>Loads an assistant (unloading the current one first, with its exit).</summary>
    public async Task LoadAsync(IAssistant assistant, string id, string package, string data, JsonElement settings, EntranceKind entrance)
    {
        if (Current is not null) await UnloadAsync(ExitKind.Switching).ConfigureAwait(true);
        if (!Watcher.IsStarted) Watcher.Start(_engine.Now, _engine.Post);
        var host = new AssistantHost(this, id, package, data, settings);
        Current = assistant;
        CurrentHost = host;
        _faults = 0;
        if (!Guard(a => a.Attach(host), "Attach") || !Guard(a => a.Enter(entrance), "Enter")) return;
        Log.Info($"Asistente: «{id}» cargado");
        RequestTick();
    }

    /// <summary>Lets the assistant leave (with a time limit) and drops it.</summary>
    public async Task UnloadAsync(ExitKind why)
    {
        if (Current is not { } assistant) return;
        var id = CurrentHost?.Id;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await assistant.ExitAsync(why, limit.Token).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Asistente: «{id}» no ha salido bien ({ex.Message})");
        }
        Drop(assistant);
        Log.Info($"Asistente: «{id}» descargado ({why})");
    }

    /// <summary>The settings of the loaded assistant changed in K-Desktop's Options.</summary>
    public void ApplySettings(JsonElement settings)
    {
        if (CurrentHost is { } host) host.Settings = settings;
        Guard(a => a.OnSettingsChanged(settings), "OnSettingsChanged");
    }

    /// <summary>K-Desktop was paused or resumed.</summary>
    public void SetPaused(bool paused) => Watcher.SetPaused(paused);

    public void PublishPanel(JsonObject data) => _link?.Send(new PanelMessage(data));

    public void Notify(string title, string text) => _link?.Send(new NotifyMessage(title, text));

    public void RequestTick() => _engine.RequestTick();

    public void Post(Action action) => _engine.Post(action);

    public void Dispose()
    {
        if (Current is { } assistant) Drop(assistant);
        Watcher.Dispose();
        Pointer?.Dispose();
        Rendering?.Dispose();
    }

    /// <summary>Calls the assistant; false when it threw (counted; several in a row unload it).</summary>
    private bool Guard(Action<IAssistant> call, string what)
    {
        if (Current is not { } assistant) return false;
        try
        {
            call(assistant);
            _faults = 0;
            return true;
        }
        catch (Exception ex)
        {
            var id = CurrentHost?.Id ?? "?";
            Log.Error($"Asistente: «{id}» ha fallado en {what}", ex);
            if (++_faults >= MaxFaults)
            {
                Log.Warn($"Asistente: «{id}» falla una y otra vez; se descarga");
                Drop(assistant);
                Faulted?.Invoke(id);
            }
            return false;
        }
    }

    private void Drop(IAssistant assistant)
    {
        var id = CurrentHost?.Id ?? "?";
        try { assistant.Dispose(); }
        catch (Exception ex) { Log.Error("Asistente: error al liberarlo", ex); }
        Current = null;
        CurrentHost = null;
        // What it left behind goes with it: surfaces, hit areas and its subscriptions to the world.
        Rendering?.DisposeSurfaces();
        Pointer?.Dispose();
        World.ForgetSubscribers();
        if (_context is { } context)
        {
            _context = null;
            // Later, from the engine queue: now the caller's frames (async ones too) still hold the assistant.
            _engine.Post(() => PackageLoader.Unload(context, id));
        }
    }

    private IEnumerable<nint> OwnWindows()
    {
        if (Rendering is not null) yield return Rendering.OverlayHandle;
        if (Pointer is null) yield break;
        foreach (var handle in Pointer.Handles) yield return handle;
    }

    private void OnMenu(string area, Point at) =>
        // The host's menu (talk, write, extras, settings…) comes with Options → Assistant (stage 1.8).
        Log.Info($"Asistente: menú pedido en «{area}» ({at.X:0},{at.Y:0})");
}
