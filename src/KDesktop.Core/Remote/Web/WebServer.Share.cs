using System.Net;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// «Compartir → K-Desktop» on the phone (phase 7, 5.0.412): what is shared reaches the PC. <c>POST api/share {text, open}</c>:
/// a text goes to the clipboard, a link with <c>open</c> opens in the PC's browser; <c>POST api/share/file?name=…</c> (the
/// bytes): the file is saved in the user's Downloads. An OSD says what arrived and from which phone.
/// </summary>
public sealed partial class WebServer
{
    private const int MaxSharedText = 32 * 1024;
    private const long MaxSharedFile = 512L * 1024 * 1024;

    private async Task ShareTextAsync(HttpListenerContext context, WebDevice device)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var text = (Str(body, "text") ?? string.Empty).Trim();
        var open = body is { } b && b.TryGetProperty("open", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
        if (text.Length is 0 or > MaxSharedText)
        {
            await WriteJsonAsync(context.Response, new { error = "badRequest" }, 400).ConfigureAwait(false);
            return;
        }
        var who = device.Name;
        if (open && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var opened = await Task.Run(() => ProcessLauncher.Launch(new LaunchSpec(uri.AbsoluteUri), asAdmin: false)).ConfigureAwait(false);
            Log.Info($"Web: «{who}» abre un enlace en el PC ({uri.Host})");
            _host.Ui?.ShowOsd(Loc.T("osd.share.opened", who, uri.Host), opened ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("Phone24"));
            await WriteJsonAsync(context.Response, new { ok = opened, did = "opened" }).ConfigureAwait(false);
            return;
        }
        var copied = await Task.Run(() => ClipboardSnapshot.SetText(text)).ConfigureAwait(false);
        Log.Info($"Web: «{who}» deja un texto en el portapapeles ({text.Length} caracteres)");
        var shown = text.Length > 80 ? text[..79] + "…" : text;
        _host.Ui?.ShowOsd(Loc.T("osd.share.copied", who, shown), copied ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("Phone24"));
        await WriteJsonAsync(context.Response, new { ok = copied, did = "copied" }).ConfigureAwait(false);
    }

    private async Task ShareFileAsync(HttpListenerContext context, WebDevice device)
    {
        var request = context.Request;
        var name = SafeName(request.QueryString["name"]);
        var folder = KnownFolders.Downloads;
        if (name is null || folder is null || !request.HasEntityBody || request.ContentLength64 > MaxSharedFile)
        {
            await WriteJsonAsync(context.Response, new { error = name is null ? "badRequest" : folder is null ? "noFolder" : "tooBig" }, 400).ConfigureAwait(false);
            return;
        }
        var path = Unique(Path.Combine(folder, name));
        var part = path + ".part";
        try
        {
            long total = 0;
            await using (var file = File.Create(part))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await request.InputStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxSharedFile) throw new InvalidDataException("tooBig");
                    await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                }
            }
            File.Move(part, path);
            Log.Info($"Web: «{device.Name}» deja {Path.GetFileName(path)} en Descargas ({total / 1024} KB)");
            _host.Ui?.ShowOsd(Loc.T("osd.share.file", device.Name, Path.GetFileName(path)), OsdKind.Success, look: OsdLook.Of("Phone24"));
            await WriteJsonAsync(context.Response, new { ok = true, did = "saved", name = Path.GetFileName(path) }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or HttpListenerException)
        {
            try { File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Log.Warn($"Web: no se pudo guardar lo que mandó «{device.Name}»: {ex.Message}");
            await WriteJsonAsync(context.Response, new { error = "save" }, 500).ConfigureAwait(false);
        }
    }

    /// <summary>Only the name, nothing that climbs folders or Windows refuses; null when nothing is left.</summary>
    internal static string? SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = name.Replace('\\', '/').Split('/')[^1];
        foreach (var c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
        clean = clean.Trim().TrimEnd('.');
        if (clean.Length > 120) clean = clean[..60] + "…" + clean[^59..];
        return clean.Length == 0 || clean is "." or ".." ? null : clean;
    }

    /// <summary>«foto.jpg», else «foto (2).jpg»…: never over another file.</summary>
    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var next = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(next)) return next;
        }
    }
}
