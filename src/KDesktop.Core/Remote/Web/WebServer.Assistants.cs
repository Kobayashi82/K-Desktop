using System.Net;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// The assistants from the phone (phase 12): who is active, their conversations as they are written (event <c>chat</c>),
/// and orders written or said to one of them. Only paired devices, like everything else.
/// </summary>
public sealed partial class WebServer
{
    private const int MaxOrder = 2000;

    private void WatchAssistants()
    {
        if (_host.Assistants is not { } assistants) return;
        assistants.Changed += () => Broadcast("assistants");
        assistants.Line += (id, line) => Broadcast("chat", new { assistant = id, line });
    }

    /// <summary>Whether the module is on, for the session (the app shows its tab).</summary>
    private bool AssistantsOn => _host.Assistants is { } a && _host.Invoke(() => a.Enabled);

    private async Task<bool> AssistantsAsync(WebRequest request, HttpListenerContext context)
    {
        if (!request.Path!.StartsWith("/api/assistants", StringComparison.Ordinal)) return false;
        var response = context.Response;
        if (_host.Assistants is not { } assistants)
        {
            await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
            return true;
        }

        switch (request.Method, request.Path)
        {
            case ("GET", "/api/assistants"):
                var (on, list) = _host.Invoke(() => (assistants.Enabled, assistants.Enabled ? assistants.List() : []));
                await WriteJsonAsync(response, new
                {
                    enabled = on,
                    assistants = list.Select(a => new { a.Id, a.Name, a.Busy, Icon = _catalog.Keep(a.Icon) }),
                }).ConfigureAwait(false);
                break;
            case ("GET", "/api/assistants/chat"):
                var id = context.Request.QueryString["id"] ?? string.Empty;
                await WriteJsonAsync(response, new { lines = _host.Invoke(() => assistants.Chat(id)) }).ConfigureAwait(false);
                break;
            case ("POST", "/api/assistants/say"):
                var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
                var text = (Str(body, "text") ?? string.Empty).Trim();
                if (text.Length is 0 or > MaxOrder)
                {
                    await WriteJsonAsync(response, new { error = "badRequest" }, 400).ConfigureAwait(false);
                    break;
                }
                var target = Str(body, "id") ?? string.Empty;
                var error = _host.Invoke(() => assistants.Send(target, text));
                await WriteJsonAsync(response, error is null ? new { ok = true } : new { error }, error is null ? 200 : 409).ConfigureAwait(false);
                break;
            case ("POST", "/api/assistants/stop"):
                var stop = await ReadJsonAsync(context.Request).ConfigureAwait(false);
                var who = Str(stop, "id") ?? string.Empty;
                _host.Invoke(() =>
                {
                    assistants.Stop(who);
                    return true;
                });
                await WriteJsonAsync(response, new { ok = true }).ConfigureAwait(false);
                break;
            default:
                await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
                break;
        }
        return true;
    }

    /// <summary>A voice order for an assistant (<c>voice?assistant=id</c>): it goes to it, not to the commands.</summary>
    private Task<RemoteVoiceResult> VoiceFor(string? assistant, float[] samples) =>
        assistant is { Length: > 0 } && _host.Assistants is { } assistants ? assistants.VoiceAsync(assistant, samples) : _host.VoiceAsync(samples);

    private void Broadcast(string name, object data)
    {
        if (_eventClients.IsEmpty) return;
        var json = JsonSerializer.Serialize(data, WebJson.Options);
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {json}\n\n");
        foreach (var (response, gate) in _eventClients) _ = WriteEventAsync(response, gate, bytes);
    }
}
