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

    /// <summary>The icon of each assistant, drawn once per installed version.</summary>
    private readonly Dictionary<string, (string Key, WebIcon? Icon)> _icons = new(StringComparer.Ordinal);

    public WebAssistants(App app)
    {
        _app = app;
        app.AssistantChanged += () => Changed?.Invoke();
        app.AssistantChatChanged += id => Line?.Invoke(id, app.AssistantChat(id) is { Count: > 0 } chat ? LineOf(chat[^1]) : null);
    }

    public event Action? Changed;

    public event Action<string, WebChatLine?>? Line;

    public bool Enabled => _app.Assistant is not null;

    public IReadOnlyList<WebAssistantInfo> List() =>
        _app.AssistantsOnDesktop.Where(App.IsAssistant)
            .Select(id => new WebAssistantInfo(id, App.AssistantName(id), _app.IsAssistantThinking(id), IconOf(id)))
            .ToList();

    public IReadOnlyList<WebChatLine> Chat(string id) => _app.AssistantChat(id).Select(LineOf).ToList();

    public string? Send(string id, string text)
    {
        if (!Enabled || !App.IsAssistant(id) || !_app.IsOnDesktop(id)) return "notActive";
        return _app.SendToAssistant(id, text, "phone") ? null : "notRunning";
    }

    public void Stop(string id)
    {
        if (Enabled && App.IsAssistant(id)) _app.StopAssistant(id);
    }

    public Task<RemoteVoiceResult> VoiceAsync(string id, float[] samples)
    {
        var to = _app.Dispatcher.Invoke(() => Enabled && App.IsAssistant(id) && _app.IsOnDesktop(id) ? _app.AddresseeFor(id) : null);
        return to is null
            ? Task.FromResult(new RemoteVoiceResult("", Core.Localization.Loc.T("assistantChat.nobody"), Core.Engine.OsdKind.Warning))
            : _app.Voice.RunRemoteAsync(samples, to);
    }

    private static WebChatLine LineOf(ChatItem item) => new(item.Role, item.Turn, item.Text, item.Final, item.At, item.Written);

    private WebIcon? IconOf(string id)
    {
        var key = AssistantTargeting.IsSystem(id) ? "system" : AssistantPackages.Find(id)?.Folder ?? string.Empty;
        if (_icons.TryGetValue(id, out var cached) && cached.Key == key) return cached.Icon;
        var icon = WebHost.Png(AssistantLook.IconOf(id));
        _icons[id] = (key, icon);
        return icon;
    }
}
