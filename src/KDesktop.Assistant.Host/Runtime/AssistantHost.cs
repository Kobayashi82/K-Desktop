using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>What the host gives one loaded assistant (<see cref="IAssistantHost"/>).</summary>
internal sealed class AssistantHost(AssistantRuntime runtime, string id, string packageDirectory, string dataDirectory, JsonElement settings)
    : IAssistantHost
{
    public string Id { get; } = id;

    public Version SdkVersion { get; } = typeof(IAssistant).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor) : new Version(1, 0);

    public string Language => Loc.Instance.Language;

    public IWorld World => runtime.World;

    public IRendering Rendering => runtime.Rendering;

    public IPointer Pointer => runtime.Pointer;

    public IKeyboard Keyboard { get; } = new HostKeyboard();

    public IAudio Audio { get; } = new SilentAudio();

    public IWindowServices Windows => runtime.WindowServices;

    public JsonElement Settings { get; internal set; } = settings;

    public string DataDirectory { get; } = dataDirectory;

    public string PackageDirectory { get; } = packageDirectory;

    public IAssistantLog Log { get; } = new PrefixedLog(id);

    public void PublishPanel(JsonObject data) => runtime.PublishPanel(Id, data);

    public void Notify(string title, string text) => runtime.Notify(title, text);

    public void RequestTick() => runtime.RequestTick();

    public void Post(Action action) => runtime.Post(action);
}

/// <summary>Lines of an assistant in assistant.log, with its id.</summary>
internal sealed class PrefixedLog(string id) : IAssistantLog
{
    public void Debug(string message)
    {
#if DEBUG
        Core.Log.Info($"[{id}] {message}");
#endif
    }

    public void Info(string message) => Core.Log.Info($"[{id}] {message}");

    public void Warn(string message, Exception? ex = null) => Core.Log.Warn($"[{id}] {message}" + (ex is null ? "" : $": {ex.Message}"));

    public void Error(string message, Exception? ex = null) => Core.Log.Error($"[{id}] {message}", ex);
}

/// <summary>
/// The keyboard: reading the state always works; capturing keys (K-Desktop swallowing them for the game) needs the
/// <c>keys</c> message, which comes with the Lander's game (phase 2). Until then capturing only reads.
/// </summary>
internal sealed partial class HostKeyboard : IKeyboard
{
    public event Action<Key, bool>? KeyChanged { add { } remove { } }

    public bool IsDown(Key key)
    {
        var vk = KeyInterop.VirtualKeyFromKey(key);
        return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public void Capture(IReadOnlyCollection<Key> keys) { }

    public void Release() { }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);
}

/// <summary>No sound yet: the host's mixer arrives with the Lander (phase 2). Sounds load and play as silence.</summary>
internal sealed class SilentAudio : IAudio
{
    public bool Enabled => false;

    public ISound Load(float[] samples, int sampleRate, int channels = 1) =>
        new Sound(TimeSpan.FromSeconds(samples.Length / (double)Math.Max(1, sampleRate * Math.Max(1, channels))));

    public ISound LoadWav(string path) => new Sound(TimeSpan.Zero);

    public void Play(ISound sound, double gain = 1, double pan = 0) { }

    public ILoop Loop(ISound sound) => new SilentLoop();

    private sealed record Sound(TimeSpan Duration) : ISound;

    private sealed class SilentLoop : ILoop
    {
        public double Gain { get; set; }
        public double Pitch { get; set; } = 1;
        public double Pan { get; set; }
        public void Dispose() { }
    }
}
