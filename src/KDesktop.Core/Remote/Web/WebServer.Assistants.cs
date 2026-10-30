using System.Collections.Concurrent;
using System.Net;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// The assistants from the phone (phase 12): who is active, their conversations as they are written (event <c>chat</c>),
/// and orders written or said to one of them. Only paired devices, like everything else. Since 5.0.410 the answer can
/// sound on the phone that asked (<c>speak</c>: <c>phone</c> or <c>both</c>): each piece of its voice is kept here a few
/// minutes, that phone hears of it with the event <c>speech</c> and fetches it with <c>api/assistants/audio</c> (WAV);
/// <c>speechEnd</c> says the voice of that turn is over.
/// </summary>
public sealed partial class WebServer
{
    private const int MaxOrder = 2000;
    private const int MaxSpeechPieces = 300;
    private static readonly TimeSpan SpeechKept = TimeSpan.FromMinutes(5);

    /// <summary>A piece of an answer's voice waiting for the phone that asked: for that device only.</summary>
    private sealed record SpeechPiece(string Device, byte[] Wav, DateTime At);

    private readonly ConcurrentDictionary<string, SpeechPiece> _speech = new(StringComparer.Ordinal);

    private void WatchAssistants()
    {
        if (_host.Assistants is not { } assistants) return;
        assistants.Changed += () => Broadcast("assistants");
        assistants.Line += (id, line) => Broadcast("chat", new { assistant = id, line });
        assistants.Speech += OnSpeech;
        assistants.Did += (id, did) => Broadcast("did", new { assistant = id, did });
    }

    private void OnSpeech(WebSpeech speech)
    {
        if (speech.End)
        {
            SendTo(speech.Device, "speechEnd", new { assistant = speech.Assistant, turn = speech.Turn, stopped = speech.Stopped });
            return;
        }
        if (speech.Pcm is not { Length: > 0 } pcm) return;
        var now = DateTime.UtcNow;
        foreach (var (key, old) in _speech)
            if (now - old.At > SpeechKept) _speech.TryRemove(key, out _);
        if (_speech.Count >= MaxSpeechPieces)
            foreach (var key in _speech.OrderBy(p => p.Value.At).Take(_speech.Count - MaxSpeechPieces + 1).Select(p => p.Key).ToList())
                _speech.TryRemove(key, out _);
        _speech[SpeechKey(speech.Turn, speech.Index)] = new SpeechPiece(speech.Device, Wav(pcm, speech.Rate), now);
        SendTo(speech.Device, "speech", new { assistant = speech.Assistant, turn = speech.Turn, n = speech.Index });
    }

    private static string SpeechKey(string turn, int index) => turn + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>16-bit mono PCM in a WAV file (what every browser and Android play without anything else).</summary>
    internal static byte[] Wav(byte[] pcm, int rate)
    {
        var wav = new byte[44 + pcm.Length];
        void Text(int at, string s) => System.Text.Encoding.ASCII.GetBytes(s).CopyTo(wav, at);
        void Int(int at, int v) => BitConverter.TryWriteBytes(wav.AsSpan(at, 4), v);
        void Short(int at, short v) => BitConverter.TryWriteBytes(wav.AsSpan(at, 2), v);
        Text(0, "RIFF");
        Int(4, 36 + pcm.Length);
        Text(8, "WAVE");
        Text(12, "fmt ");
        Int(16, 16);
        Short(20, 1);
        Short(22, 1);
        Int(24, rate);
        Int(28, rate * 2);
        Short(32, 2);
        Short(34, 16);
        Text(36, "data");
        Int(40, pcm.Length);
        pcm.CopyTo(wav, 44);
        return wav;
    }

    /// <summary>Whether the module is on, for the session (the app shows its tab).</summary>
    private bool AssistantsOn => _host.Assistants is { } a && _host.Invoke(() => a.Enabled);

    /// <summary>Where the phone wants the answer: <c>phone</c>, <c>both</c>, anything else on the PC.</summary>
    private static string? SpeakOf(string? speak) => speak is "phone" or "both" ? speak : null;

    private async Task<bool> AssistantsAsync(WebRequest request, HttpListenerContext context, WebDevice device)
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
                var (lines, did) = _host.Invoke(() => (assistants.Chat(id), assistants.Activity(id)));
                await WriteJsonAsync(response, new { lines, did }).ConfigureAwait(false);
                break;
            case ("GET", "/api/assistants/audio"):
                await SpeechAudioAsync(context, device).ConfigureAwait(false);
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
                var speak = SpeakOf(Str(body, "speak"));
                var error = _host.Invoke(() => assistants.Send(target, text, device.Id, speak));
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

    /// <summary>GET api/assistants/audio?turn=…&amp;n=…: a piece of the voice, only for the device it was said for.</summary>
    private async Task SpeechAudioAsync(HttpListenerContext context, WebDevice device)
    {
        var turn = context.Request.QueryString["turn"] ?? string.Empty;
        var found = int.TryParse(context.Request.QueryString["n"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && _speech.TryGetValue(SpeechKey(turn, n), out var piece) && piece.Device == device.Id ? piece : null;
        if (found is null)
        {
            await WriteJsonAsync(context.Response, new { error = "notFound" }, 404).ConfigureAwait(false);
            return;
        }
        var response = context.Response;
        response.ContentType = "audio/wav";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = found.Wav.Length;
        await response.OutputStream.WriteAsync(found.Wav).ConfigureAwait(false);
    }

    /// <summary>A voice order for an assistant (<c>voice?assistant=id</c>): it goes to it, not to the commands.</summary>
    private Task<RemoteVoiceResult> VoiceFor(string? assistant, float[] samples, string device, string? speak) =>
        assistant is { Length: > 0 } && _host.Assistants is { } assistants
            ? assistants.VoiceAsync(assistant, samples, device, SpeakOf(speak))
            : _host.VoiceAsync(samples);
}
