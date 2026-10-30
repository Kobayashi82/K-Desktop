using System.Net;
using System.Text.Json;
using KDesktop.Core.Input;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

public sealed partial class WebServer
{
    /// <summary>GET: volume, mute, playback devices and the volume of each program.</summary>
    private async Task AudioStateAsync(HttpListenerResponse response) =>
        await WriteJsonAsync(response, await Task.Run(AudioState).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// POST {volume} | {muted} | {app, volume | muted} (one program) | {device} | {media: "playPause" | "next" | "previous"} | {key: "left" | "right" | "up" |
    /// "down" | "space"}: applies it and answers with the new state. The keys go to the window in front (a player:
    /// seek, its own volume, pause), so only when «Touchpad y teclado» is allowed.
    /// </summary>
    private async Task AudioChangeAsync(HttpListenerContext context)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var state = await Task.Run(() =>
        {
            if (body is not { } b) return AudioState();
            try
            {
                int? volume = b.TryGetProperty("volume", out var v) && v.TryGetInt32(out var level) ? Math.Clamp(level, 0, 100) : null;
                bool? muted = b.TryGetProperty("muted", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False ? m.GetBoolean() : null;
                if (Str(b, "app") is { Length: > 0 } app)
                {
                    // One program, as in the volume mixer.
                    if (volume is not null || muted is not null)
                        AppVolume.Change(app, now => new LevelState(volume ?? now.Level, muted ?? (volume is > 0 ? false : now.Muted)));
                }
                else
                {
                    if (volume is { } all) AudioTools.SetVolume(all);
                    if (muted is { } mute) AudioTools.SetMute(mute);
                }
                if (b.TryGetProperty("device", out var d) && d.GetString() is { Length: > 0 } id)
                {
                    // Like the Windows settings: every role, communications included.
                    if (AudioDevices.Playback().Any(x => x.Id == id && x.IsActive)) AudioDevices.SetDefault(id, communications: true);
                }
                if (b.TryGetProperty("media", out var media))
                {
                    var vk = media.GetString() switch
                    {
                        "playPause" => 0xB3,
                        "next" => 0xB0,
                        "previous" => 0xB1,
                        _ => 0,
                    };
                    if (vk != 0) InputSimulator.KeyPress(vk);
                }
                if (b.TryGetProperty("key", out var key) && InputAllowed)
                {
                    var (vk, extended) = key.GetString() switch
                    {
                        "left" => (0x25, true),
                        "up" => (0x26, true),
                        "right" => (0x27, true),
                        "down" => (0x28, true),
                        "space" => (0x20, false),
                        _ => (0, false),
                    };
                    if (vk != 0) InputSimulator.KeyPress(vk, extended);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Web: error cambiando el audio: " + ex.Message);
            }
            return AudioState();
        }).ConfigureAwait(false);
        await WriteJsonAsync(context.Response, state).ConfigureAwait(false);
    }

    private object AudioState()
    {
        int? volume = null;
        var muted = false;
        try
        {
            volume = AudioTools.GetVolume();
            muted = AudioTools.IsMuted();
        }
        catch (Exception ex)
        {
            // No playback device at all.
            Log.Warn("Web: no se pudo leer el volumen: " + ex.Message);
        }
        return new
        {
            volume,
            muted,
            devices = AudioDevices.Playback().Select(d => new
            {
                d.Id,
                d.Name,
                d.Adapter,
                Active = d.IsActive,
                Default = d.IsDefault,
                Kind = d.FormFactor.ToString().ToLowerInvariant(),
            }),
            apps = AppLevels(),
        };
    }

    /// <summary>The programs with sound open and their volume, each with its icon.</summary>
    private object[] AppLevels()
    {
        IReadOnlyList<AppLevel> apps;
        try { apps = AppVolume.List(); }
        catch (Exception ex)
        {
            Log.Warn("Web: no se pudo leer el volumen de los programas: " + ex.Message);
            return [];
        }
        return [.. apps.Select(a => new
        {
            a.Process,
            a.Name,
            a.Level,
            a.Muted,
            a.Active,
            Icon = a.Path is null ? null : _catalog.Keep(_host.FileIcon(a.Path)),
        })];
    }
}
