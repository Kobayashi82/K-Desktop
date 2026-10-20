using System.Text.Json;
using System.Text.Json.Nodes;

namespace KDesktop.Assistant.Sdk;

/// <summary>Everything the host offers an assistant. Use it from the engine thread unless a member says otherwise.</summary>
public interface IAssistantHost
{
    /// <summary>SDK version of the host («1.0»).</summary>
    Version SdkVersion { get; }

    /// <summary>Interface language of K-Desktop («es», «en»…). Changes restart the assistant.</summary>
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

    /// <summary>Asks for a tick as soon as possible. Can be called from any thread.</summary>
    void RequestTick();

    /// <summary>Runs <paramref name="action"/> on the engine thread. Can be called from any thread.</summary>
    void Post(Action action);

    IAssistantLog Log { get; }
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
