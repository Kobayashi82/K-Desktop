using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>
/// The Bot API over HTTPS (telegram, phase 1): getUpdates with long polling, sending texts, files, voice notes and
/// «escribiendo…», editing and deleting, and downloading files. The token goes only in the address and never in a message
/// or the log.
/// </summary>
public sealed class TelegramBotConnection : ITelegramConnection
{
    /// <summary>How long a getUpdates waits for something to arrive.</summary>
    public const int PollSeconds = 50;

    private const string Api = "https://api.telegram.org/";

    private readonly string _token;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private long _offset;

    /// <param name="http">For the tests; by default its own client, with a timeout longer than the long polling.</param>
    public TelegramBotConnection(string token, HttpClient? http = null)
    {
        _token = token.Trim();
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(PollSeconds + 25) };
    }

    public TelegramCapabilities Capabilities => TelegramCapabilities.Bot;

    /// <summary>Whether it looks like a bot's token («123456:ABC…»), before asking Telegram.</summary>
    public static bool LooksLikeToken(string token)
    {
        var t = token.Trim();
        var colon = t.IndexOf(':', StringComparison.Ordinal);
        return colon > 3 && t[..colon].All(char.IsAsciiDigit) && t.Length - colon > 20 && t[(colon + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    public async Task<TelegramUser> GetMeAsync(CancellationToken cancel)
    {
        var me = await CallAsync("getMe", [], cancel).ConfigureAwait(false);
        return User(me);
    }

    /// <summary>Forgets a webhook the bot had (with one, getUpdates is refused), keeping what is waiting.</summary>
    public Task DeleteWebhookAsync(CancellationToken cancel) => CallAsync("deleteWebhook", [], cancel);

    public async Task<IReadOnlyList<TelegramIncoming>> ReceiveAsync(CancellationToken cancel)
    {
        var result = await CallAsync("getUpdates",
        [
            new("offset", _offset.ToString(CultureInfo.InvariantCulture)),
            new("timeout", PollSeconds.ToString(CultureInfo.InvariantCulture)),
            new("allowed_updates", "[\"message\",\"callback_query\"]"),
        ], cancel).ConfigureAwait(false);
        var list = new List<TelegramIncoming>();
        foreach (var update in result.EnumerateArray())
        {
            var id = update.GetProperty("update_id").GetInt64();
            _offset = Math.Max(_offset, id + 1);
            if (ParseUpdate(update) is { } message) list.Add(message);
        }
        return list;
    }

    /// <summary>An update as a message (or a button pressed); null when it is not one of a person (a channel post, an edit, a bot…).</summary>
    public static TelegramIncoming? ParseUpdate(JsonElement update)
    {
        if (update.TryGetProperty("callback_query", out var callback)) return ParseButton(update, callback);
        if (!update.TryGetProperty("message", out var m) || !m.TryGetProperty("from", out var from)) return null;
        if (from.TryGetProperty("is_bot", out var bot) && bot.GetBoolean()) return null;
        var chat = m.GetProperty("chat");
        var kind = Str(chat, "type") switch
        {
            "private" => TelegramChatKind.Private,
            "channel" => TelegramChatKind.Channel,
            _ => TelegramChatKind.Group,
        };
        var text = Str(m, "text") ?? Str(m, "caption") ?? string.Empty;
        var content = TelegramContent.Text;
        TelegramFile? file = null;
        if (m.TryGetProperty("voice", out var voice))
        {
            content = TelegramContent.Voice;
            file = FileOf(voice, null);
        }
        else if (m.TryGetProperty("photo", out var photo) && photo.GetArrayLength() > 0)
        {
            content = TelegramContent.Photo;
            file = FileOf(photo[photo.GetArrayLength() - 1], null); // the biggest size comes last
        }
        else if (m.TryGetProperty("document", out var doc))
        {
            content = TelegramContent.Document;
            file = FileOf(doc, Str(doc, "file_name"));
        }
        else if (!m.TryGetProperty("text", out _)) content = TelegramContent.Other;
        long? replyTo = m.TryGetProperty("reply_to_message", out var reply) && reply.TryGetProperty("from", out var replyFrom) ? replyFrom.GetProperty("id").GetInt64() : null;
        return new TelegramIncoming(
            update.GetProperty("update_id").GetInt64(),
            chat.GetProperty("id").GetInt64(),
            kind,
            Str(chat, "title"),
            m.GetProperty("message_id").GetInt32(),
            User(from),
            DateTimeOffset.FromUnixTimeSeconds(m.TryGetProperty("date", out var date) ? date.GetInt64() : 0),
            text,
            content,
            file,
            replyTo);
    }

    private static TelegramIncoming? ParseButton(JsonElement update, JsonElement callback)
    {
        if (!callback.TryGetProperty("from", out var from) || !callback.TryGetProperty("message", out var m) || !m.TryGetProperty("chat", out var chat)) return null;
        var kind = Str(chat, "type") == "private" ? TelegramChatKind.Private : TelegramChatKind.Group;
        return new TelegramIncoming(
            update.GetProperty("update_id").GetInt64(),
            chat.GetProperty("id").GetInt64(),
            kind,
            Str(chat, "title"),
            m.GetProperty("message_id").GetInt32(),
            User(from),
            DateTimeOffset.UtcNow,
            string.Empty,
            TelegramContent.Other,
            CallbackId: Str(callback, "id"),
            CallbackData: Str(callback, "data") ?? string.Empty);
    }

    private static TelegramFile FileOf(JsonElement f, string? name) =>
        new(Str(f, "file_id") ?? string.Empty, name, Str(f, "mime_type"), f.TryGetProperty("file_size", out var size) ? size.GetInt64() : 0);

    private static TelegramUser User(JsonElement u)
    {
        var name = ((Str(u, "first_name") ?? string.Empty) + " " + (Str(u, "last_name") ?? string.Empty)).Trim();
        return new TelegramUser(u.GetProperty("id").GetInt64(), name, Str(u, "username"));
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<int> SendTextAsync(long chat, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken cancel)
    {
        List<KeyValuePair<string, string>> args = [new("chat_id", Id(chat)), new("text", text), new("link_preview_options", "{\"is_disabled\":true}")];
        if (buttons is { Count: > 0 }) args.Add(new("reply_markup", Keyboard(buttons)));
        var sent = await CallAsync("sendMessage", [.. args], cancel).ConfigureAwait(false);
        return sent.GetProperty("message_id").GetInt32();
    }

    /// <summary>Rows of buttons as Telegram's inline keyboard.</summary>
    public static string Keyboard(IReadOnlyList<IReadOnlyList<TelegramButton>> rows) => JsonSerializer.Serialize(new
    {
        inline_keyboard = rows.Select(row => row.Select(b => new { text = b.Text, callback_data = b.Data })),
    });

    public Task AnswerButtonAsync(string callbackId, string? text, CancellationToken cancel) =>
        CallAsync("answerCallbackQuery", text is { Length: > 0 } ? [new("callback_query_id", callbackId), new("text", text)] : [new("callback_query_id", callbackId)], cancel);

    public async Task<int> SendFileAsync(long chat, TelegramFileKind kind, string path, string? caption, CancellationToken cancel)
    {
        var (method, field) = kind switch
        {
            TelegramFileKind.Photo => ("sendPhoto", "photo"),
            TelegramFileKind.Voice => ("sendVoice", "voice"),
            _ => ("sendDocument", "document"),
        };
        var info = new FileInfo(path);
        if (!info.Exists) throw new TelegramException(Loc.T("telegram.error.noFile", path));
        if (info.Length > Capabilities.MaxUpload) throw new TelegramException(Loc.T("telegram.error.tooBig", Path.GetFileName(path)));
        using var form = new MultipartFormDataContent { { new StringContent(Id(chat)), "chat_id" } };
        if (caption is { Length: > 0 }) form.Add(new StringContent(caption), "caption");
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(kind == TelegramFileKind.Voice ? "audio/ogg" : "application/octet-stream");
            form.Add(content, field, Path.GetFileName(path));
            var sent = await PostAsync(method, form, cancel).ConfigureAwait(false);
            return sent.GetProperty("message_id").GetInt32();
        }
    }

    public Task SendChatActionAsync(long chat, string action, CancellationToken cancel) =>
        CallAsync("sendChatAction", [new("chat_id", Id(chat)), new("action", action)], cancel);

    public Task EditTextAsync(long chat, int message, string text, CancellationToken cancel) =>
        CallAsync("editMessageText", [new("chat_id", Id(chat)), new("message_id", message.ToString(CultureInfo.InvariantCulture)), new("text", text)], cancel);

    public Task DeleteAsync(long chat, int message, CancellationToken cancel) =>
        CallAsync("deleteMessage", [new("chat_id", Id(chat)), new("message_id", message.ToString(CultureInfo.InvariantCulture))], cancel);

    public async Task DownloadAsync(string fileId, string destination, CancellationToken cancel)
    {
        var file = await CallAsync("getFile", [new("file_id", fileId)], cancel).ConfigureAwait(false);
        if (Str(file, "file_path") is not { } path) throw new TelegramException(Loc.T("telegram.error.download"));
        using var response = await _http.GetAsync(new Uri($"{Api}file/bot{_token}/{path}"), HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new TelegramException(Loc.T("telegram.error.download"), (int)response.StatusCode);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var output = File.Create(destination);
        await using (output.ConfigureAwait(false))
            await response.Content.CopyToAsync(output, cancel).ConfigureAwait(false);
    }

    private static string Id(long chat) => chat.ToString(CultureInfo.InvariantCulture);

    private Task<JsonElement> CallAsync(string method, KeyValuePair<string, string>[] args, CancellationToken cancel) =>
        PostAsync(method, new FormUrlEncodedContent(args), cancel);

    private async Task<JsonElement> PostAsync(string method, HttpContent content, CancellationToken cancel)
    {
        using var _ = content;
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(new Uri($"{Api}bot{_token}/{method}"), content, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Without the address: it has the token.
            throw new HttpRequestException(Loc.T("telegram.error.network"), null, ex.StatusCode);
        }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return Parse(body, (int)response.StatusCode);
        }
    }

    /// <summary>The result of an answer, or the failure it says (with the wait asked for when it is too fast).</summary>
    public static JsonElement Parse(string body, int status)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { throw new TelegramException(Loc.T("telegram.error.answer", status), status); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean() && root.TryGetProperty("result", out var result)) return result.Clone();
            var code = root.TryGetProperty("error_code", out var c) && c.TryGetInt32(out var n) ? n : status;
            TimeSpan? wait = root.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var r) && r.TryGetInt32(out var s) ? TimeSpan.FromSeconds(s) : null;
            var description = Str(root, "description") ?? string.Empty;
            var message = code switch
            {
                401 or 404 => Loc.T("telegram.error.badToken"),
                409 => Loc.T("telegram.error.conflict"),
                429 => Loc.T("telegram.error.tooFast"),
                403 => Loc.T("telegram.error.blocked"),
                _ => description.Length > 0 ? description : Loc.T("telegram.error.answer", code),
            };
            throw new TelegramException(message, code, wait);
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
