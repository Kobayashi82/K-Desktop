using System.Text.Json;
using System.Text.Json.Nodes;

namespace KDesktop.Assistant.Sdk;

/// <summary>Everything the host offers an assistant. Use it from the engine thread unless a member says otherwise.</summary>
public interface IAssistantHost
{
    /// <summary>SDK version of the host («1.0»).</summary>
    Version SdkVersion { get; }

    /// <summary>
    /// The language it speaks in («es», «en»…): the one the user gave it in its settings, or the one all the assistants
    /// speak in. Changes load the assistant again.
    /// </summary>
    string Language { get; }

    IWorld World { get; }

    IRendering Rendering { get; }

    IPointer Pointer { get; }

    IKeyboard Keyboard { get; }

    IAudio Audio { get; }

    IWindowServices Windows { get; }

    /// <summary>Its settings: the values of its settings.json, with defaults filled in.</summary>
    JsonElement Settings { get; }

    /// <summary>
    /// The settings every assistant has the same way (SDK 1.9), set by the user in the «Basics» tab of its card: those
    /// its manifest names in <c>common</c>; the rest stay as usual. When they change, <see cref="IAssistant.OnSettingsChanged"/>
    /// is called. The volume of its sounds is not here: the host applies it.
    /// </summary>
    CommonSettings Common { get; }

    /// <summary>
    /// Its own data folder (progress, statistics…): kept when it is updated or switched, included in K-Desktop's
    /// backups, and asked about when it is uninstalled.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>Folder where its package was unpacked (read only: images, sounds…).</summary>
    string PackageDirectory { get; }

    /// <summary>
    /// Publishes the data its panel.json binds to (Panel → Assistant; also the images of a gallery with «source» in its
    /// settings). It is merged with what it published before: objects key by key, anything else (numbers, texts,
    /// lists) replaces. Publish everything in <see cref="IAssistant.Enter"/>; after that, what changed is enough. Cheap
    /// to call: the host sends it at most four times a second.
    /// </summary>
    void PublishPanel(JsonObject data);

    /// <summary>Shows a short notice from it (K-Desktop's toast), e.g. «Achievement unlocked».</summary>
    void Notify(string title, string text);

    /// <summary>
    /// Shows, greys out or hides one of its extras in its right-click menu and its panel buttons, by what makes sense
    /// right now («Repeat the race» only after a race). Every extra is <see cref="ExtraState.Shown"/> each time it is
    /// loaded. Commands and voice still reach <see cref="IAssistant.OnExtraAsync"/>: say there why it cannot be done.
    /// Cheap: only changes are sent. (SDK 1.1.)
    /// </summary>
    void SetExtraState(string extraId, ExtraState state);

    /// <summary>
    /// The hotkey of the K-Desktop command that runs one of its extras, as K-Desktop shows it («CTRL + ALT + L»), or null
    /// when no command with a hotkey runs it. Up to date whenever the user changes the commands. (SDK 1.1.)
    /// </summary>
    string? ExtraHotkey(string extraId);

    /// <summary>
    /// The situation its voice speaks in (SDK 1.2): <c>error</c> (it crashed), <c>happy</c> (it celebrates) or any other
    /// its voice profile names in its <c>moods</c>; null, as usual. The effects of that mood are added to what it says from
    /// then on. Wherever it is, it sounds as if it were on the monitor in use.
    /// </summary>
    void SetVoiceMood(string? mood);

    /// <summary>Asks for a tick as soon as possible. Can be called from any thread.</summary>
    void RequestTick();

    /// <summary>Runs <paramref name="action"/> on the engine thread. Can be called from any thread.</summary>
    void Post(Action action);

    IAssistantLog Log { get; }
}

/// <summary>The settings every assistant has the same way (<see cref="IAssistantHost.Common"/>, SDK 1.9).</summary>
/// <param name="Size">
/// How big the user wants it, as a factor of the size it was drawn for: 0.5 … 2 (1 = as designed). Multiply your sizes by
/// it (and by the monitor's scaling, as always).
/// </param>
/// <param name="Liveliness">
/// How much it moves about on its own: 0 = only when it is asked something, 1 = as usual, up to 2 = very lively. Scale how
/// often it wanders, plays, explores…
/// </param>
/// <param name="Dragging">
/// Whether the user lets it be picked up with the mouse. When not, the host keeps the drags of its <c>body</c> hit area
/// from it (other hit areas, a toy for instance, can still be dragged).
/// </param>
public sealed record CommonSettings(double Size = 1, double Liveliness = 1, bool Dragging = true)
{
    /// <summary>As usual.</summary>
    public static CommonSettings Default { get; } = new();
}

/// <summary>The desktop as the host sees it.</summary>
public interface IWorld
{
    WorldSnapshot Current { get; }

    SystemState System { get; }

    /// <summary>A new snapshot was made (windows moved, opened, closed; monitors changed).</summary>
    event Action<WorldSnapshot>? Changed;

    /// <summary>
    /// Asks for a quicker update of the windows for a while (the assistant is standing on one that moves). The host
    /// rescans at most once per frame until <paramref name="seconds"/> pass.
    /// </summary>
    void WatchClosely(double seconds);

    /// <summary>
    /// Listens to the rhythm of what the speakers play (SDK 1.7), to dance on the beat: <see cref="Music"/> is only
    /// measured while some assistant listens. It reads the output's peak meter (the bar of Windows' volume mixer: nothing
    /// is recorded), a hundred times a second while something sounds and a few times while it is quiet.
    /// </summary>
    void ListenToMusic(bool listen);

    /// <summary>The music playing now (SDK 1.7; not playing unless this assistant listens: <see cref="ListenToMusic"/>).</summary>
    MusicBeat Music { get; }

    /// <summary>
    /// The other assistants on the desktop and where their bodies are (SDK 1.10), so that it does not land, stop or stroll
    /// on top of one of them: passing in front is fine, staying there is not. <see cref="WorldExtensions.TakenBy"/> says
    /// whether a place is taken. Up to date after every tick of theirs.
    /// </summary>
    IReadOnlyList<OtherAssistant> Others { get; }
}

/// <summary>Another assistant on the desktop (<see cref="IWorld.Others"/>, SDK 1.10).</summary>
/// <param name="Id">Its id («lander»…).</param>
/// <param name="Body">Where its body is, in screen pixels (<see cref="IAssistant.BodyBounds"/>).</param>
/// <param name="Moving">It is on its way somewhere (it moved in the last half second): it will not stay there.</param>
public sealed record OtherAssistant(string Id, PixelRect Body, bool Moving);

public static class WorldExtensions
{
    /// <summary>
    /// The other assistant staying over <paramref name="area"/> (where this one would be), or null if the place is free.
    /// One on its way somewhere does not count unless <paramref name="moving"/>.
    /// </summary>
    public static OtherAssistant? TakenBy(this IWorld world, PixelRect area, bool moving = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var other in world.Others)
            if ((moving || !other.Moving) && other.Body.Intersects(area)) return other;
        return null;
    }
}

/// <summary>Sound. The host mixes everything and applies the user's volume for assistants.</summary>
public interface IAudio
{
    bool Enabled { get; }

    /// <summary>Loads mono or stereo float samples (−1…1). Keep the result: loading is the expensive part.</summary>
    ISound Load(float[] samples, int sampleRate, int channels = 1);

    /// <summary>Loads a WAV file from its package (relative to <see cref="IAssistantHost.PackageDirectory"/>).</summary>
    ISound LoadWav(string path);

    /// <summary>Plays a sound once. <paramref name="pan"/> −1 (left) … 1 (right).</summary>
    void Play(ISound sound, double gain = 1, double pan = 0);

    /// <summary>A sound that repeats until disposed (an engine, a hum) with live gain, pitch and pan.</summary>
    ILoop Loop(ISound sound);
}

public interface ISound
{
    TimeSpan Duration { get; }
}

public interface ILoop : IDisposable
{
    double Gain { get; set; }

    /// <summary>Playback speed (1 = normal); changes the pitch too.</summary>
    double Pitch { get; set; }

    double Pan { get; set; }
}

/// <summary>Its lines go to K-Desktop's assistant log, prefixed with its id.</summary>
public interface IAssistantLog
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message, Exception? ex = null);
    void Error(string message, Exception? ex = null);
}
