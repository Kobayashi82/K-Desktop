using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Stage;

/// <summary>
/// <c>KDesktop.exe --assistant-showcase &lt;package&gt;</c> (phase 16): one assistant on the showcase's stage, a desktop of its
/// own drawn off the screen. Started by K-Desktop for the «Probar» tab of its card (with <c>--pipe</c>: K-Desktop asks for
/// each thing and shows the stage's pictures live), or alone with <c>--record</c> to record a clip of each thing for the
/// catalog (<see cref="StageRecorder"/>). Nothing of it lies over the user's screen, it touches none of the user's windows,
/// and the assistant gets a data folder of its own that is thrown away.
/// </summary>
internal sealed class StageSession : IDisposable
{
    private int _shuttingDown;
    private StageFrames? _frames;
    /// <summary>Loads and unloads one after another (engine thread).</summary>
    private Task _changes = Task.CompletedTask;

    public StageSession(AssistantOptions options, Dispatcher ui)
    {
        Options = options;
        Ui = ui;
        Folder = options.Showcase!;
        Data = options.StageData ?? Path.Combine(Path.GetTempPath(), "K-Desktop", "Stage", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (options.Pipe is { } pipe) Link = new KDesktopLink(pipe, optional: false);
        Runtime = new AssistantRuntime(Engine, Link, stage: true);
        Runtime.LanguageOf = Personal.LanguageOf;
        Choreography = new ModuleChoreography(Link, Engine, Runtime);
        Showcase = new ModuleShowcase(Link is { } link ? m => link.Send(m) : null, Runtime, Runtime.Stage!, Choreography, ReplayAsync, () => Settled);
    }

    public AssistantOptions Options { get; }

    public Dispatcher Ui { get; }

    /// <summary>The package (its folder: installed, or the build's <c>package</c> folder).</summary>
    public string Folder { get; }

    /// <summary>Its data folder on the stage: its own, emptied when it starts and deleted when it ends.</summary>
    public string Data { get; }

    public EngineLoop Engine { get; } = new();

    public KDesktopLink? Link { get; }

    public AssistantRuntime Runtime { get; }

    public ModuleChoreography Choreography { get; }

    public ModuleShowcase Showcase { get; }

    public ModulePersonal Personal { get; } = new();

    public StageScene Scene => Runtime.Stage!;

    /// <summary>The assistant's id, once loaded.</summary>
    public string? Id { get; private set; }

    public int ExitCode { get; private set; }

    public void Start()
    {
        try
        {
            if (Directory.Exists(Data)) Directory.Delete(Data, recursive: true);
        }
        catch (IOException) { }
        Engine.Started += () =>
        {
            Runtime.Start();
            Log.Info($"Escenario: motor en marcha con {Folder}");
            if (Link is null) Then(StartRecordingAsync);
        };
        Engine.Stopping += Runtime.Dispose;
        Engine.Tick = Runtime.Tick;
        Engine.Schedule = Runtime.NextTick;
        Engine.Start();

        if (Link is not { } link) return;
        Runtime.StageFramed += (picture, _) => _frames?.Write(picture);
        link.Connected += welcome =>
        {
            Personal.Configure(welcome.Config?["personal"] as JsonObject);
            var general = welcome.Config?["general"] as JsonObject;
            var common = welcome.Config?["common"] as JsonObject;
            Engine.Post(() =>
            {
                Scene.Dark = welcome.Theme == "dark";
                if (general is not null) ApplyGeneral(general);
                Runtime.ApplyCommon(common);
                _frames ??= new StageFrames(StageScene.Width, StageScene.Height, m => link.Send(m));
                // Already there (as after a reload), not making its entrance: «Probar» is ready at once, and its entrances
                // are a choice like the rest.
                Then(() => LoadAsync(EntranceKind.Reloaded, defaults: false));
                Runtime.RedrawStage();
            });
        };
        link.Received += message => Engine.Post(() => OnMessage(message));
        link.Ended += reason => Shutdown(reason);
        link.Start();
    }

    private void OnMessage(PipeMessage message)
    {
        switch (message)
        {
            case ShowcaseMessage showcase:
                Showcase.OnShowcase(showcase);
                break;
            case StageMessage { Op: StageMessage.Reset }:
                Scene.Reset();
                break;
            case StageMessage stage:
                Runtime.StageInput?.OnStage(stage);
                break;
            case ConfigMessage { Section: "assistantSettings", Assistant: { } id, Value: { } value }:
                Runtime.ApplySettings(id, JsonSerializer.SerializeToElement(value));
                break;
            case ConfigMessage { Section: "general", Value: JsonObject general }:
                ApplyGeneral(general);
                break;
            case ConfigMessage { Section: "common", Value: JsonObject common }:
                Runtime.ApplyCommon(common);
                break;
            case ConfigMessage { Section: "personal", Value: JsonObject personal }:
                Personal.Configure(personal);
                break;
        }
    }

    /// <summary>Its sound and the speed of its manoeuvres, as on the desktop.</summary>
    private void ApplyGeneral(JsonObject general)
    {
        var sound = general["sound"] is JsonValue on && on.TryGetValue<bool>(out var b) ? b : true;
        var volume = general["volume"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 1;
        Runtime.Audio.Apply(sound, volume);
        if (general["manoeuvres"] is JsonValue m && m.TryGetValue<string>(out var speed)) Choreography.Speed = speed;
    }

    /// <summary>Puts the assistant on the stage, and tells K-Desktop (<c>loaded</c>).</summary>
    private async Task LoadAsync(EntranceKind entrance, bool defaults)
    {
        try
        {
            // Its world first (the stage's monitor, taskbar and windows): an assistant places itself in Enter, and with an
            // empty world it would land off the stage.
            Scene.Update(Engine.Now);
            Id = await Runtime.LoadPackageAsync(Folder, Data, entrance, defaults).ConfigureAwait(true);
            Link?.Send(new LoadedMessage(Id, true));
        }
        catch (Exception ex)
        {
            Log.Error($"Escenario: no se ha podido cargar {Folder}", ex);
            Link?.Send(new LoadedMessage(Id ?? Path.GetFileName(Folder), false, ex.Message));
            if (Link is null) Shutdown("no se ha podido cargar", 2);
        }
    }

    /// <summary>An entrance always shows this long, even when its body does not move (waking up where it stands).</summary>
    private static readonly TimeSpan EnteringAtLeast = TimeSpan.FromSeconds(1.5);

    /// <summary>The longest an entrance is waited for (a lander dropping from the sky takes about 4.5 s).</summary>
    private static readonly TimeSpan EnteringAtMost = TimeSpan.FromSeconds(7);

    /// <summary>Loads and unloads done, so the assistant is on the stage again (or for good not).</summary>
    public Task Settled => _changes.ContinueWith(_ => { }, TaskScheduler.Default);

    /// <summary>
    /// Takes it off the stage without a goodbye, so that an entrance is recorded from an empty stage (<see cref="ReplayAsync"/>
    /// then only brings it in).
    /// </summary>
    public Task TakeAwayAsync(string id)
    {
        Then(async () =>
        {
            try
            {
                if (Runtime.IsLoaded(id)) await Runtime.UnloadAsync(id, ExitKind.Reloading).ConfigureAwait(true);
            }
            catch (Exception ex) { Log.Error($"Escenario: no se ha podido quitar a «{id}» antes de su entrada", ex); }
        });
        return Settled;
    }

    /// <summary>
    /// An entrance or an exit: it leaves as <paramref name="why"/> says and comes back as <paramref name="how"/> says. An
    /// entrance is over when it has come in and stopped moving; an exit when it has gone (and <paramref name="away"/> after: it
    /// comes back meanwhile, see <see cref="Settled"/>).
    /// </summary>
    private Task<bool> ReplayAsync(string id, ExitKind why, EntranceKind how, TimeSpan away)
    {
        if (id != Id) return Task.FromResult(false);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Then(async () =>
        {
            try
            {
                if (Runtime.IsLoaded(id)) await Runtime.UnloadAsync(id, why).ConfigureAwait(true);
                await Task.Delay(away).ConfigureAwait(true);
                if (how == EntranceKind.Reloaded) done.TrySetResult(true);
                await Runtime.LoadPackageAsync(Folder, Data, how, defaultSettings: Link is null).ConfigureAwait(true);
                if (how != EntranceKind.Reloaded) await EnteredAsync(id).ConfigureAwait(true);
                done.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Log.Error($"Escenario: «{id}» no ha vuelto tras enseñar su {(how == EntranceKind.Reloaded ? why : how)}", ex);
                Link?.Send(new LoadedMessage(id, false, ex.Message));
                done.TrySetResult(false);
            }
        });
        return done.Task;
    }

    /// <summary>Waits for its entrance: a moment at least, then until its body stops (or a few seconds).</summary>
    private async Task EnteredAsync(string id)
    {
        var start = Engine.Now;
        await Task.Delay(EnteringAtLeast).ConfigureAwait(true);
        while (Runtime.IsMoving(id) && Engine.Now - start < EnteringAtMost.TotalSeconds) await Task.Delay(100).ConfigureAwait(true);
    }

    /// <summary>Recording, with nobody to talk to: the clips, and the process ends (<see cref="ExitCode"/> says how it went).</summary>
    private async Task StartRecordingAsync()
    {
        Runtime.Audio.Apply(false, 0);
        Choreography.Speed = "normal";
        Scene.DrawCursor = true;
        await LoadAsync(EntranceKind.Startup, defaults: true).ConfigureAwait(true);
        if (Id is not { } id) return;
        _ = RecordAsync(id);
    }

    private async Task RecordAsync(string id)
    {
        var code = 0;
        try
        {
            var recorder = new StageRecorder(this, Options.Record!, Options.Manifest);
            code = await recorder.RunAsync(id).ConfigureAwait(true) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Error("Escenario: la grabación ha fallado", ex);
            Console.Error.WriteLine(ex.Message);
            code = 3;
        }
        _ = Task.Run(() => Shutdown("grabación terminada", code));
    }

    private void Then(Func<Task> change) => _changes = After(_changes, change);

    private static async Task After(Task previous, Func<Task> change)
    {
        try { await previous.ConfigureAwait(true); }
        catch { /* logged where it failed */ }
        await change().ConfigureAwait(true);
    }

    /// <summary>Closes the stage: the assistant leaves at once, the engine stops and the UI thread ends. Only the first call counts.</summary>
    public void Shutdown(string reason, int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        Log.Info($"Escenario: cerrando ({reason})");
        ExitCode = exitCode;
        var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.Post(async () =>
        {
            try { await Runtime.UnloadAllAsync(ExitKind.Shutdown); }
            finally { left.TrySetResult(); }
        });
        left.Task.Wait(TimeSpan.FromSeconds(3.5));
        Engine.Stop(TimeSpan.FromSeconds(3));
        Ui.BeginInvokeShutdown(DispatcherPriority.Normal);
    }

    public void Dispose()
    {
        Shutdown("fin");
        if (Link is { } link)
        {
            try { Task.Run(async () => await link.DisposeAsync()).Wait(TimeSpan.FromSeconds(2)); } catch { /* closing anyway */ }
        }
        _frames?.Dispose();
        Engine.Dispose();
        try
        {
            if (Directory.Exists(Data)) Directory.Delete(Data, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
