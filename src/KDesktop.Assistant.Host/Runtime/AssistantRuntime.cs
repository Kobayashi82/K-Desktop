using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using KDesktop.Assistant.Host.Audio;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Host.Stage;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant;
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
    /// <summary>The common settings of each assistant (stage 13.13), as K-Desktop last sent them (by id: they come before its load).</summary>
    private Dictionary<string, AssistantCommon> _common = new(StringComparer.Ordinal);
    private double _nextOnTop;

    /// <summary>The language each assistant speaks in (its own, or the process's): what its <c>host.Language</c> says.</summary>
    public Func<string, string>? LanguageOf { get; set; }

    /// <param name="stage">
    /// The showcase's stage (phase 16): the assistants live on a desktop of their own, drawn off the screen, instead of the
    /// user's.
    /// </param>
    public AssistantRuntime(EngineLoop engine, KDesktopLink? link, bool stage = false)
    {
        _engine = engine;
        _link = link;
        _panel = new PanelPublisher(message => _link?.Send(message));
        World = new HostWorld(() => engine.Now);
        World.Changed += snapshot => Each(slot => Guard(slot, _ => slot.World.Raise(snapshot), "World.Changed"));
        Watcher = new WorldWatcher(World, () => engine.Now, OwnWindows, e => Each(slot => Guard(slot, a => a.OnSystemEvent(e), "OnSystemEvent")), engine.RequestTick);
        if (stage)
        {
            Stage = new StageScene(World, () => engine.Now, link?.Theme == "dark");
            WindowServices = Stage;
            StageInput = new StageInput(Stage, () => _slots.AsEnumerable().Reverse().Where(s => !s.Dropped).SelectMany(s => s.Pointer.VirtualAreas));
        }
        else WindowServices = new HostWindowServices(Watcher.Scanner, message => _link?.Send(message) == true);
    }

    /// <summary>The showcase's stage (phase 16), or null on the user's desktop.</summary>
    public StageScene? Stage { get; }

    /// <summary>The user's pointer over the stage's picture (phase 16), or null on the user's desktop.</summary>
    public StageInput? StageInput { get; }

    /// <summary>A new picture of the stage (engine thread): its pixels stay valid until the next one.</summary>
    public event Action<StageCompositor, double>? StageFramed;

    /// <summary>Pictures of the stage a second, at most.</summary>
    public double StageFrameRate { get; set; } = 30;

    private StageCompositor? _compositor;
    private double _nextStageFrame;

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

    /// <summary>Whether its body moved a moment ago (or just appeared), as <see cref="IWorld.Others"/> says it.</summary>
    public bool IsMoving(string id) => Find(id) is { Body: not null } slot && _engine.Now - slot.MovedAt < MovingFor;

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
        Rendering = new HostRendering(offscreen: Stage is not null);
        if (Stage is not null) _compositor = new StageCompositor(StageScene.Width, StageScene.Height);
        Faulted += id => _link?.Send(new LoadedMessage(id, false, "falla una y otra vez"));
        Watcher.MonitorsChanged += Rendering.UpdateBounds;
    }

    /// <summary>One engine tick: ticks the assistants that are due and returns the seconds it may sleep.</summary>
    public double Tick(double now)
    {
        var tickStart = RenderStats.Now();
        Rendering.Pacer.Update(now);
        var interval = 60.0;
        if (Stage is { } stage) interval = Math.Min(interval, stage.Update(now));
        if (Watcher.IsStarted)
        {
            var world = RenderStats.Now();
            interval = Watcher.Update(now);
            RenderStats.Add(RenderStats.Part.World, world);
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
                var assistant = RenderStats.Now();
                if (!Guard(slot, a => wanted = a.Tick(new TickInfo(now, elapsed)), "Tick")) wanted = 1;
                RenderStats.Add(RenderStats.Part.Assistants, assistant);
                slot.LastTick = now;
                slot.Due = now + (double.IsFinite(wanted) ? Math.Clamp(wanted, 0, 60) : 1);
                ReadBody(slot, now);
            }
            if (!slot.Dropped) interval = Math.Min(interval, Math.Max(0, slot.Due - now));
            if (slot.MenuUntil > 0)
            {
                if (now >= slot.MenuUntil) MenuClosed(slot.Id);
                else interval = Math.Min(interval, slot.MenuUntil - now);
            }
        }
        if (now >= _nextOnTop)
        {
            // Other «always on top» windows may cover it: take the top back every few seconds, without fighting.
            _nextOnTop = now + 5;
            Rendering.KeepOnTop();
            foreach (var slot in _slots) slot.Pointer.KeepOnTop();
        }
        foreach (var slot in _slots.ToList())
        {
            if (slot.Bubble is { } bubble) interval = Math.Min(interval, UpdateBubble(slot, bubble, now));
            if (!slot.SpeechShown) continue;
            if (now >= slot.SpeechDropAt) HideOwnSpeech(slot);
            else interval = Math.Min(interval, slot.SpeechDropAt - now);
        }
        interval = Math.Min(interval, PublishBodies(now));
        Rendering.Commit();
        if (Stage is not null) interval = Math.Min(interval, PresentStage(now));
        RenderStats.Tick(tickStart);
        return interval;
    }

    /// <summary>
    /// The stage's picture after a tick in which something changed, at most <see cref="StageFrameRate"/> a second; returns
    /// when the next one is due if one is waiting.
    /// </summary>
    private double PresentStage(double now)
    {
        var stage = Stage!;
        if (!Rendering.Changed && !stage.Changed) return 60;
        if (now < _nextStageFrame) return _nextStageFrame - now;
        _nextStageFrame = now + 1 / Math.Max(1, StageFrameRate);
        Rendering.Changed = false;
        var backdrop = stage.Backdrop(now);
        if (_compositor!.Compose(Rendering, stage.Bounds, backdrop, stage.CursorDrawing()))
        {
            try { StageFramed?.Invoke(_compositor, now); }
            catch (Exception ex) { Log.Error("Asistente: error con la imagen del escenario", ex); }
        }
        return 60;
    }

    /// <summary>A picture of the stage on the next tick even if nothing moved (a recording that starts).</summary>
    public void RedrawStage()
    {
        if (Stage is { } stage) stage.Changed = true;
        _nextStageFrame = 0;
        _engine.RequestTick();
    }

    /// <summary>How long a body counts as moving after its last move (s).</summary>
    private const double MovingFor = 0.5;

    /// <summary>
    /// How far its body's centre must go to count as a move (px): breathing, bobbing or an arm that waves are not walking
    /// away.
    /// </summary>
    private const double MoveThreshold = 6;

    /// <summary>Where its body is after a tick (its own word for it, or its body hit area).</summary>
    private void ReadBody(AssistantSlot slot, double now)
    {
        PixelRect? body = null;
        if (!Guard(slot, a => body = a.BodyBounds, "BodyBounds")) return;
        body ??= slot.Pointer.BodyBounds;
        if (body is { IsEmpty: true }) body = null;
        if (body is { } b && (slot.Body is null || (b.Center - slot.MovedFrom).Length > MoveThreshold))
        {
            // Appearing counts as arriving: still on its way for a moment.
            slot.MovedFrom = b.Center;
            slot.MovedAt = now;
        }
        slot.Body = body;
    }

    /// <summary>
    /// Every body on the desktop, for <see cref="IWorld.Others"/> (stage 13.14); returns when a «moving» runs out, so that
    /// it is up to date even if nobody ticks.
    /// </summary>
    private double PublishBodies(double now)
    {
        var bodies = new List<OtherAssistant>(_slots.Count);
        var wake = 60.0;
        foreach (var slot in _slots)
        {
            if (slot.Dropped || slot.Body is not { } body) continue;
            var left = slot.MovedAt + MovingFor - now;
            if (left > 0) wake = Math.Min(wake, left);
            bodies.Add(new OtherAssistant(slot.Id, body, left > 0));
        }
        World.Publish(bodies);
        return wake;
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
    public async Task<string> LoadPackageAsync(string folder, string? data, EntranceKind entrance, bool defaultSettings = false)
    {
        var package = PackageLoader.Load(folder, data, defaultSettings);
        await LoadAsync(package.Assistant, package.Manifest.Id, package.Folder, package.DataFolder, package.Settings, entrance,
            package.Manifest, package.Context).ConfigureAwait(true);
        return package.Manifest.Id;
    }

    /// <summary>Loads an assistant next to the others (one with its id leaves first, as reloading).</summary>
    public async Task LoadAsync(IAssistant assistant, string id, string package, string data, JsonElement settings, EntranceKind entrance,
        AssistantManifest? manifest = null, AssemblyLoadContext? context = null)
    {
        if (Find(id) is not null) await UnloadAsync(id, ExitKind.Reloading).ConfigureAwait(true);
        if (Stage is null && !Watcher.IsStarted) Watcher.Start(_engine.Now, _engine.Post);
        var slot = new AssistantSlot(id, assistant, manifest, context, Rendering, World) { LastTick = _engine.Now };
        slot.Pointer = new HostPointer(e =>
            {
                if (!slot.LetsThrough(e)) return;
                Guard(slot, a => a.OnPointer(e), "OnPointer");
            }, (area, at) => OnMenu(slot, at), UpdateWheel, _ => OnWrite(slot), (_, active) => OnTalk(slot, active),
            (_, files, at) => OnDropped(slot, files, at), at => OnDropLabel(slot, at))
        {
            Name = manifest?.Name ?? id,
            Virtual = Stage is not null,
        };
        slot.Keyboard = new HostKeyboard(keys => _link?.Send(new KeysMessage(id, [.. keys.Select(k => k.ToString())])));
        slot.Audio = new AssistantAudio(Audio, package);
        slot.SetCommon(CommonOf(id, manifest));
        slot.Host = new AssistantHost(this, slot, package, data, settings);
        _slots.Add(slot);
        BodiesChanged();
        if (!Guard(slot, a => a.Attach(slot.Host), "Attach") || !Guard(slot, a => a.Enter(entrance), "Enter"))
        {
            // Half attached it would only fail again on every tick: it does not stay.
            Drop(slot);
            throw new InvalidOperationException($"«{id}» ha fallado al entrar en el escritorio (detalles en assistant.log)");
        }
        Log.Info($"Asistente: «{id}» cargado ({_slots.Count} en el escritorio, {entrance})");
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

    /// <summary>
    /// The common settings of every assistant (the <c>common</c> section of <c>config</c>, stage 13.13): those on the desktop
    /// whose values change get them at once (their volume, and <see cref="IAssistant.OnSettingsChanged"/>).
    /// </summary>
    public void ApplyCommon(JsonObject? section)
    {
        if (section is null) return;
        _common = AssistantCommon.FromJson(section);
        foreach (var slot in _slots.ToList())
        {
            var now = CommonOf(slot.Id, slot.Manifest);
            if (now == slot.Common) continue;
            slot.SetCommon(now);
            Log.Info($"Asistente: ajustes comunes de «{slot.Id}»: {Describe(now)}");
            // K-Desktop's bubble from now on: what it was showing itself goes (the next answer comes in the bubble).
            if (!slot.OwnSpeech) HideOwnSpeech(slot);
            Guard(slot, a => a.OnSettingsChanged(slot.Host.Settings), "OnSettingsChanged");
        }
    }

    /// <summary>What an assistant gets of the common settings: those its manifest takes; the others as usual.</summary>
    private AssistantCommon CommonOf(string id, AssistantManifest? manifest)
    {
        if (manifest is null || _common.GetValueOrDefault(id) is not { } set) return AssistantCommon.Default;
        var usual = AssistantCommon.Default;
        return new AssistantCommon
        {
            Size = manifest.Takes(AssistantCommon.SizeKey) ? set.Size : usual.Size,
            Volume = manifest.Takes(AssistantCommon.VolumeKey) ? set.Volume : usual.Volume,
            Liveliness = manifest.Takes(AssistantCommon.LivelinessKey) ? set.Liveliness : usual.Liveliness,
            Dragging = manifest.Takes(AssistantCommon.DraggingKey) ? set.Dragging : usual.Dragging,
            KDesktopBubble = manifest.OwnSpeech && set.KDesktopBubble,
        };
    }

    private static string Describe(AssistantCommon c) => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"tamaño {c.Size:P0}, volumen {c.Volume:P0}, movido {c.Liveliness:P0}, {(c.Dragging ? "se puede coger" : "sin cogerlo")}{(c.KDesktopBubble ? ", bocadillo de K-Desktop" : "")}");

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
    /// What the module is doing for an assistant (thinking, speaking…) and with what (stage 13.4), for the character to
    /// show it. One built before SDK 1.13 gets the closest state it knows (its interface does that).
    /// </summary>
    public void SetState(string id, AssistantState state, StateDetail? detail = null)
    {
        if (Find(id) is { } slot) Guard(slot, a => a.SetState(state, detail ?? StateDetail.None), "SetState");
    }

    /// <summary>
    /// The character «says» <paramref name="text"/> (Communicate) for <paramref name="duration"/>, or less if cancelled or it
    /// leaves. One that cannot do it now (asleep in its game…) just lets it pass. Engine thread.
    /// </summary>
    public async Task CommunicateAsync(string id, string text, TimeSpan duration, CancellationToken cancellationToken, Task? speaking = null)
    {
        if (Find(id) is not { } slot) return;
        var turn = ++slot.SpeechTurn;
        slot.AheadTurn = null;
        slot.SpeechDropAt = double.PositiveInfinity;
        // What it says, over the character for as long as it takes to read, whatever the gesture: its own text if it draws
        // it and can now (stage 13.7), K-Desktop's bubble otherwise.
        slot.Bubble?.Dispose();
        slot.Bubble = null;
        if (!(slot.OwnSpeech && ShowOwnSpeech(slot, new SpeechText(text, true, _engine.Now + duration.TotalSeconds))))
        {
            HideOwnSpeech(slot);
            ShowBubble(slot, text, duration.TotalSeconds);
        }
        _engine.RequestTick();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, slot.Extras.Token);
        limit.CancelAfter(duration);
        try
        {
            await GestureAsync(slot, new Gesture.Communicate(text, null), duration, limit.Token).ConfigureAwait(true);
            // Its voice (stage 5.4) may take longer than reading: the text stays until it has said it (a minute at most).
            var until = _engine.Now + 60;
            while (speaking is { IsCompleted: false } && !cancellationToken.IsCancellationRequested && !slot.Extras.IsCancellationRequested && _engine.Now < until
                && slot.SpeechTurn == turn)
            {
                KeepSaying(slot, text);
                await Task.WhenAny(speaking, Task.Delay(250, CancellationToken.None)).ConfigureAwait(true);
            }
        }
        finally
        {
            // Unless something newer is being said already.
            if (slot.SpeechTurn == turn)
            {
                slot.Bubble?.Close(_engine.Now);
                HideOwnSpeech(slot);
            }
            _engine.RequestTick();
        }
    }

    private void ShowBubble(AssistantSlot slot, string text, double seconds) =>
        slot.Bubble = new HostBubble(Rendering.CreateSurface(SurfaceLayer.Front), text, _link?.Theme == "dark", _engine.Now, seconds);

    /// <summary>
    /// Its voice is still saying it: the text stays a little longer. Its own is told so about once a second; if it cannot
    /// any more, K-Desktop's bubble takes over.
    /// </summary>
    private void KeepSaying(AssistantSlot slot, string text)
    {
        var now = _engine.Now;
        if (slot.Bubble is { } bubble)
        {
            bubble.KeepUntil(now + 0.6);
            return;
        }
        if (!slot.SpeechShown || slot.SpeechUntil - now > 1) return;
        if (slot.OwnSpeech && ShowOwnSpeech(slot, new SpeechText(text, true, now + 2))) return;
        HideOwnSpeech(slot);
        ShowBubble(slot, text, 0.6);
        _engine.RequestTick();
    }

    /// <summary>Asks it to show what it says itself; false: it cannot (or failed), and nothing of its own stays shown.</summary>
    private bool ShowOwnSpeech(AssistantSlot slot, SpeechText speech)
    {
        var shown = false;
        if (!Guard(slot, a => shown = a.ShowSpeech(speech), "ShowSpeech")) shown = false;
        if (!shown)
        {
            if (slot.SpeechShown) Log.Info($"Asistente: «{slot.Id}» no puede enseñar lo que dice: bocadillo de K-Desktop");
            HideOwnSpeech(slot);
            return false;
        }
        if (!slot.SpeechShown) Log.Info($"Asistente: «{slot.Id}» enseña lo que dice con su propio texto");
        slot.SpeechShown = true;
        slot.SpeechUntil = speech.Until;
        return true;
    }

    private void HideOwnSpeech(AssistantSlot slot)
    {
        if (!slot.SpeechShown) return;
        slot.SpeechShown = false;
        slot.SpeechDropAt = double.PositiveInfinity;
        Guard(slot, a => a.HideSpeech(), "HideSpeech");
    }

    /// <summary>How long an answer shown while it is written stays if its end never comes (s).</summary>
    private const double AheadFor = 20;

    /// <summary>
    /// The answer of <paramref name="turn"/> so far, while it is written (stage 13.7): one that draws its own text shows it
    /// as it grows. K-Desktop's bubble waits for the whole answer, as always. Engine thread.
    /// </summary>
    public void SpeechAhead(string id, string turn, string text)
    {
        if (Find(id) is not { } slot || !slot.OwnSpeech || text.Length == 0) return;
        if (slot.AheadTurn != turn)
        {
            // A new answer: what was being said before (and its end) is no longer this one.
            if (slot.Bubble is not null) return;
            slot.SpeechTurn++;
            slot.AheadTurn = turn;
        }
        if (ShowOwnSpeech(slot, new SpeechText(text, false, _engine.Now + AheadFor))) slot.SpeechDropAt = _engine.Now + AheadFor;
        _engine.RequestTick();
    }

    /// <summary>The order was cancelled: an answer shown while written, which will not be finished, goes. Engine thread.</summary>
    public void StopSpeech(string id)
    {
        if (Find(id) is { } slot && slot.AheadTurn is not null) HideOwnSpeech(slot);
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

    /// <summary>Where its bubble points (screen pixels; null: it is not here or does not say). Engine thread.</summary>
    public Point? AnchorOf(string id)
    {
        if (Find(id) is not { } slot) return null;
        Point? anchor = null;
        Guard(slot, a => anchor = a.BubbleAnchor, "BubbleAnchor");
        return anchor;
    }

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
        _dropLabel?.Dispose();
        _compositor?.Dispose();
        Watcher.Dispose();
        World.Dispose();
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

    /// <summary>Its own animations for the showcase (SDK 1.18; none if it is not there or fails). Engine thread.</summary>
    public IReadOnlyList<ShowcaseItem> ShowcaseOf(string id)
    {
        IReadOnlyList<ShowcaseItem> items = [];
        if (Find(id) is { } slot) Guard(slot, a => items = a.Showcase ?? [], "Showcase");
        return items;
    }

    /// <summary>Plays one of its own showcase items: its task, or null when it is not there or failed to start it. Engine thread.</summary>
    public Task<bool>? PlayShowcase(string id, string item, CancellationToken token)
    {
        if (Find(id) is not { } slot) return null;
        Task<bool>? running = null;
        return Guard(slot, a => running = a.PlayShowcaseAsync(item, token), "PlayShowcaseAsync " + item) ? running : null;
    }

    /// <summary>Where its body is now (what it said after its last tick), or null. Engine thread.</summary>
    public PixelRect? BodyOf(string id) => Find(id)?.Body;

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

    /// <summary>
    /// Files dropped on an assistant (stage 13.8): it already got <see cref="PointerEventKind.FilesDropped"/> to catch them;
    /// K-Desktop decides what to do with them.
    /// </summary>
    private void OnDropped(AssistantSlot slot, IReadOnlyList<string> files, Point at)
    {
        if (slot.Dropped) return;
        Log.Info($"Asistente: a «{slot.Id}» le sueltan {files.Count} archivo(s) ({string.Join(", ", files.Take(3).Select(Path.GetFileName))}{(files.Count > 3 ? "…" : "")})");
        _link?.Send(new DroppedMessage(slot.Id, files, (int)Math.Round(at.X), (int)Math.Round(at.Y)));
    }

    /// <summary>The label by the cursor while files are dragged over an assistant (SDK 1.17; shared, one drag at a time).</summary>
    private DropLabel? _dropLabel;

    /// <summary>Files dragged over an assistant: «Give it to X» follows the cursor (Windows' own picture is hidden there).</summary>
    private void OnDropLabel(AssistantSlot slot, Point? at)
    {
        if (at is not { } point || slot.Dropped)
        {
            _dropLabel?.Hide();
            return;
        }
        if (Rendering is null) return;
        _dropLabel ??= new DropLabel(Rendering.CreateSurface(SurfaceLayer.Front));
        var monitor = World.Current.MonitorAt(point) ?? World.Current.PrimaryMonitor;
        _dropLabel.Show(Loc.T("assistant.drop.give", slot.Pointer.Name), point, _link?.Theme == "dark", monitor);
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
        // «Always on top» and moving with the body, its hit windows would end up over the menu, which opens right there,
        // and a click on them takes the mouse from it (WPF closes it): they step aside while it is open.
        slot.Pointer.Suspended = true;
        slot.MenuUntil = _engine.Now + MenuTimeout;
        MenuRequested?.Invoke(new MenuRequest(slot.Id, name, extras, at));
    }

    /// <summary>The longest a menu keeps the hit areas away, if K-Desktop never says it closed.</summary>
    private const double MenuTimeout = 60;

    /// <summary>Its menu closed (<c>menuClosed</c>, or the host's own one): its hit areas come back.</summary>
    public void MenuClosed(string assistant)
    {
        if (Find(assistant) is not { } slot || slot.MenuUntil == 0) return;
        slot.MenuUntil = 0;
        slot.Pointer.Suspended = false;
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

    public WorldView World { get; } = new(world, id);

    /// <summary>Where its body is (stage 13.14), as last read after a tick; null off screen.</summary>
    public PixelRect? Body { get; set; }

    /// <summary>Where its body was when it last counted as moving, and when (engine clock).</summary>
    public Point MovedFrom { get; set; }

    public double MovedAt { get; set; } = double.NegativeInfinity;

    public HostPointer Pointer { get; set; } = null!;

    public HostKeyboard Keyboard { get; set; } = null!;

    public AssistantAudio Audio { get; set; } = null!;

    public CancellationTokenSource Extras { get; } = new();

    /// <summary>Its common settings (stage 13.13), as K-Desktop keeps them.</summary>
    public AssistantCommon Common { get; private set; } = AssistantCommon.Default;

    /// <summary>The same, as the SDK gives them (<see cref="IAssistantHost.Common"/>).</summary>
    public CommonSettings CommonSettings { get; private set; } = CommonSettings.Default;

    private readonly DragGate _drags = new();

    public void SetCommon(AssistantCommon common)
    {
        Common = common;
        CommonSettings = new CommonSettings(common.Size, common.Liveliness, common.Dragging);
        Audio.Volume = common.Volume;
    }

    /// <summary>
    /// Whether a pointer event reaches it: not the drags of its body while the user does not let it be picked up, nor the
    /// files dragged over it if it was built before SDK 1.16 (it does not know those kinds; K-Desktop still gets the files and the
    /// label by the cursor still says it takes them).
    /// </summary>
    public bool LetsThrough(PointerEvent e) =>
        _drags.LetsThrough(e, Common.Dragging) && (e.Kind < PointerEventKind.FilesOver || KnowsFiles);

    /// <summary>Files dropped on it come as pointer events (SDK 1.16; the host's own characters and test assistants too).</summary>
    private bool KnowsFiles => Manifest is not { } manifest || manifest.IsCharacter || manifest.SdkMajor > 1 || manifest.SdkMinor >= 16;

    /// <summary>The module's bubble over it while it says something (stage 3.6), or null.</summary>
    public HostBubble? Bubble { get; set; }

    /// <summary>
    /// It draws what it says itself (stage 13.7): its manifest says <c>"speech": "own"</c> and the user did not choose
    /// K-Desktop's bubble for it.
    /// </summary>
    public bool OwnSpeech => Manifest?.OwnSpeech == true && !Common.KDesktopBubble;

    /// <summary>It is showing what it says itself now (<see cref="IAssistant.ShowSpeech"/> said yes).</summary>
    public bool SpeechShown { get; set; }

    /// <summary>When it was told to hide it (engine clock, its <see cref="SpeechText.Until"/>).</summary>
    public double SpeechUntil { get; set; }

    /// <summary>
    /// When an answer shown while it is written goes on its own if its end never comes (a cancelled order); infinite
    /// while the host says it (<see cref="AssistantRuntime.CommunicateAsync"/> hides it).
    /// </summary>
    public double SpeechDropAt { get; set; } = double.PositiveInfinity;

    /// <summary>Counts what it starts to say: an older answer that ends does not hide a newer one.</summary>
    public int SpeechTurn { get; set; }

    /// <summary>The turn of the answer it shows while written (<see cref="AssistantRuntime.SpeechAhead"/>).</summary>
    public string? AheadTurn { get; set; }

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

    /// <summary>Its menu is open (its hit areas step aside) until K-Desktop says it closed or, at the latest, this time.</summary>
    public double MenuUntil { get; set; }

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
internal sealed class WorldView(HostWorld world, string id) : IWorld
{
    public WorldSnapshot Current => world.Current;

    public IReadOnlyList<OtherAssistant> Others
    {
        get
        {
            var all = world.Others;
            if (!ReferenceEquals(all, _othersOf)) (_othersOf, _others) = (all, [.. all.Where(o => o.Id != id)]);
            return _others;
        }
    }

    private IReadOnlyList<OtherAssistant>? _othersOf;
    private IReadOnlyList<OtherAssistant> _others = [];

    public SystemState System => world.System;

    public event Action<WorldSnapshot>? Changed;

    public void WatchClosely(double seconds) => world.WatchClosely(seconds);

    private bool _listening;

    public MusicBeat Music => _listening ? world.Music : default;

    public void ListenToMusic(bool listen)
    {
        _listening = listen;
        world.Listen(this, listen);
    }

    internal void Raise(WorldSnapshot snapshot) => Changed?.Invoke(snapshot);

    internal void Forget()
    {
        Changed = null;
        if (_listening) ListenToMusic(false);
    }
}
