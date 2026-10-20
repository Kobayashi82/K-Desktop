using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// What the host gives one loaded assistant (<see cref="IAssistantHost"/>): the shared overlay and world through views
/// of its own (<see cref="AssistantSlot"/>), so what it leaves behind goes with it.
/// </summary>
internal sealed class AssistantHost(AssistantRuntime runtime, AssistantSlot slot, string packageDirectory, string dataDirectory, JsonElement settings)
    : IAssistantHost
{
    public string Id { get; } = slot.Id;

    public Version SdkVersion { get; } = typeof(IAssistant).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor) : new Version(1, 0);

    public string Language => Loc.Instance.Language;

    public IWorld World { get; } = slot.World;

    public IRendering Rendering { get; } = slot.Surfaces;

    public IPointer Pointer { get; } = slot.Pointer;

    public IKeyboard Keyboard => slot.Keyboard;

    public IAudio Audio => slot.Audio;

    public IWindowServices Windows => runtime.WindowServices;

    public JsonElement Settings { get; internal set; } = settings;

    public string DataDirectory { get; } = dataDirectory;

    public string PackageDirectory { get; } = packageDirectory;

    public IAssistantLog Log { get; } = new PrefixedLog(slot.Id);

    public void PublishPanel(JsonObject data) => runtime.PublishPanel(Id, data);

    public void Notify(string title, string text) => runtime.Notify(Id, title, text);

    public void SetExtraState(string extraId, ExtraState state) => runtime.SetExtraState(slot, extraId, state);

    public string? ExtraHotkey(string extraId) => runtime.ExtraHotkey(Id, extraId);

    public void RequestTick() => runtime.RequestTick(slot);

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
/// The keyboard of one assistant. Reading works for any key; captured keys are swallowed by K-Desktop's hook (the
/// <c>keys</c> message), so Windows no longer sees them pressed: their state comes from K-Desktop's <c>key</c> messages.
/// Without K-Desktop's hook (paused, hooks off) nothing is swallowed and the state is read as usual.
/// </summary>
internal sealed partial class HostKeyboard(Action<IReadOnlyCollection<Key>> capture) : IKeyboard
{
    private HashSet<Key> _captured = [];
    private readonly HashSet<Key> _down = [];

    public event Action<Key, bool>? KeyChanged;

    public bool IsCapturing => _captured.Count > 0;

    public bool IsDown(Key key)
    {
        if (_down.Contains(key)) return true;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public void Capture(IReadOnlyCollection<Key> keys)
    {
        var next = keys.Where(k => k != Key.None).ToHashSet();
        if (next.SetEquals(_captured)) return;
        _captured = next;
        _down.IntersectWith(next);
        capture(next);
    }

    public void Release() => Capture([]);

    /// <summary>K-Desktop's <c>key</c> (engine thread).</summary>
    internal void OnKey(Key key, bool down)
    {
        if (down && !_captured.Contains(key)) return;
        if (down ? _down.Add(key) : _down.Remove(key)) KeyChanged?.Invoke(key, down);
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);
}
