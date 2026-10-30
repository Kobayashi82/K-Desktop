using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Remote.Web;
using KDesktop.Core.Voice;
using KDesktop.Views;

namespace KDesktop.Services;

/// <summary>
/// The assistants for the web app (phase 12): the active ones, the same conversations as Panel → Assistant, and orders from
/// the phone (source <c>phone</c>: its answer is not shown again on the PC's OSD, the phone shows it). UI thread.
/// </summary>
internal sealed class WebAssistants : IWebAssistants
{
    private readonly App _app;

    /// <summary>The phone that sent each one its last order, where the voice of its answer goes (any thread).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _devices = new(StringComparer.Ordinal);

    /// <summary>The icon of each assistant, drawn once per installed version.</summary>
    private readonly Dictionary<string, (string Key, WebIcon? Icon)> _icons = new(StringComparer.Ordinal);

    public WebAssistants(App app)
    {
        _app = app;
        app.AssistantChanged += () => Changed?.Invoke();
        app.AssistantChatChanged += id => Line?.Invoke(id, app.AssistantChat(id) is { Count: > 0 } chat ? LineOf(chat[^1]) : null);
        app.AssistantSpeech += OnSpeech;
        app.AssistantDid += item =>
        {
            if (DidOf(item) is { } did) Did?.Invoke(App.FromProcess(item.Assistant), did);
        };
    }

    public event Action<string, WebDid>? Did;

    /// <summary>Its last 60 lines of activity, oldest first.</summary>
    public IReadOnlyList<WebDid> Activity(string id) =>
        [.. _app.AssistantActivity.Where(item => App.IsActivityOf(item, id)).Take(60).Select(DidOf).OfType<WebDid>().Reverse()];

    private static WebDid? DidOf(ActivityItem item) =>
        string.IsNullOrWhiteSpace(item.Entry.Did) ? null : new WebDid(item.Entry.Did.Trim(), item.Entry.Ok, item.Entry.At);

    public event Action<WebSpeech>? Speech;

    private void OnSpeech(Core.Assistant.Pipe.PipeMessage message)
    {
        var (assistant, turn) = message switch
        {
            Core.Assistant.Pipe.SpeechAudioMessage audio => (audio.Assistant, audio.Turn),
            Core.Assistant.Pipe.SpeechEndMessage end => (end.Assistant, end.Turn),
            _ => (null, string.Empty),
        };
        var id = App.FromProcess(assistant);
        if (!_devices.TryGetValue(id, out var device)) return;
        if (message is Core.Assistant.Pipe.SpeechAudioMessage piece)
        {
            byte[] pcm;
            try { pcm = Convert.FromBase64String(piece.Pcm); }
            catch (FormatException) { return; }
            Speech?.Invoke(new WebSpeech(device, id, turn, piece.Index, piece.Rate, pcm));
        }
        else if (message is Core.Assistant.Pipe.SpeechEndMessage over)
        {
            Speech?.Invoke(new WebSpeech(device, id, turn, 0, 0, null, End: true, Stopped: over.Stopped));
        }
    }

    /// <summary>The next order of <paramref name="id"/> comes from <paramref name="device"/>: where its answer sounds.</summary>
    private void FromPhone(string id, string? device, string? speak)
    {
        _app.PhoneSpeak[id] = device is null ? null : speak;
        if (device is not null && speak is not null) _devices[id] = device;
    }

    public event Action? Changed;

    public event Action<string, WebChatLine?>? Line;

    public bool Enabled => _app.Assistant is not null;

    public IReadOnlyList<WebAssistantInfo> List() =>
        _app.AssistantsOnDesktop.Where(App.IsAssistant)
            .Select(id => new WebAssistantInfo(id, App.AssistantName(id), _app.IsAssistantThinking(id), IconOf(id)))
            .ToList();

    public IReadOnlyList<WebChatLine> Chat(string id) => _app.AssistantChat(id).Select(LineOf).ToList();

    public string? Send(string id, string text, string? device = null, string? speak = null)
    {
        if (!Enabled || !App.IsAssistant(id) || !_app.IsOnDesktop(id)) return "notActive";
        FromPhone(id, device, speak);
        return _app.SendToAssistant(id, text, "phone") ? null : "notRunning";
    }

    public void Stop(string id)
    {
        if (Enabled && App.IsAssistant(id)) _app.StopAssistant(id);
    }

    public Task<RemoteVoiceResult> VoiceAsync(string id, float[] samples, string? device = null, string? speak = null)
    {
        var to = _app.Dispatcher.Invoke(() => Enabled && App.IsAssistant(id) && _app.IsOnDesktop(id) ? _app.AddresseeFor(id) : null);
        if (to is not null) FromPhone(id, device, speak);
        return to is null
            ? Task.FromResult(new RemoteVoiceResult("", Core.Localization.Loc.T("assistantChat.nobody"), Core.Engine.OsdKind.Warning))
            : _app.Voice.RunRemoteAsync(samples, to);
    }

    private static WebChatLine LineOf(ChatItem item) => new(item.Role, item.Turn, item.Text, item.Final, item.At, item.Written,
        item.Images.Count > 0 ? [.. item.Images.Select(KeyOf)] : null);

    /// <summary>A picture's key for the phone: the path is never sent.</summary>
    private static string KeyOf(string path) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..20];

    /// <summary>Only a picture that is in that conversation, by its key.</summary>
    public string? Image(string id, string key) =>
        Enabled && App.IsAssistant(id) && key.Length > 0
            ? _app.AssistantChat(id).SelectMany(i => i.Images).FirstOrDefault(p => KeyOf(p) == key)
            : null;

    private WebIcon? IconOf(string id)
    {
        var key = AssistantTargeting.IsSystem(id) ? "system" : AssistantPackages.Find(id)?.Folder ?? string.Empty;
        if (_icons.TryGetValue(id, out var cached) && cached.Key == key) return cached.Icon;
        var icon = WebHost.Png(AssistantLook.IconOf(id));
        _icons[id] = (key, icon);
        return icon;
    }
}
