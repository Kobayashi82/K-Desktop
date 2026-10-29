using System.Net;
using System.Text.Json;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// What K-Desktop sends to the phone (B11, <see cref="PhoneInbox"/>): «inbox» in the event stream when something new
/// waits, api/inbox for this device, api/inbox/ack once shown, and api/inbox/file/{id} for its file.
/// </summary>
public sealed partial class WebServer
{
    private void WatchInbox()
    {
        PhoneInbox.Current.Devices = Devices.List;
        PhoneInbox.Current.Added += () => Broadcast("inbox");
    }

    /// <summary>Answers the inbox's requests; false when the request is not one of them.</summary>
    private static async Task<bool> InboxAsync(WebRequest request, HttpListenerContext context, WebDevice device)
    {
        var response = context.Response;
        var path = request.Path!;
        if (request.Method == "GET" && path == "/api/inbox")
        {
            var messages = PhoneInbox.Current.For(device.Id).Select(m => new
            {
                m.Id,
                At = m.At.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                m.Title,
                m.Text,
                m.Url,
                File = m.File is { } name ? new { Name = name, Size = m.FileSize } : null,
                m.From,
            });
            await WriteJsonAsync(response, new { messages }).ConfigureAwait(false);
            return true;
        }
        if (request.Method == "POST" && path == "/api/inbox/ack")
        {
            var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
            var ids = body is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("ids", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList()
                : [];
            PhoneInbox.Current.Ack(device.Id, ids);
            await WriteJsonAsync(response, new { ok = true }).ConfigureAwait(false);
            return true;
        }
        if (request.Method == "GET" && path.StartsWith("/api/inbox/file/", StringComparison.Ordinal))
        {
            if (PhoneInbox.Current.FileOf(path["/api/inbox/file/".Length..], device.Id) is not { } file)
            {
                await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
                return true;
            }
            response.ContentType = "application/octet-stream";
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Content-Disposition"] = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(file))}";
            await using var stream = File.OpenRead(file);
            response.ContentLength64 = stream.Length;
            await stream.CopyToAsync(response.OutputStream).ConfigureAwait(false);
            return true;
        }
        return false;
    }
}
