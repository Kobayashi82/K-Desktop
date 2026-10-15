using System.Net;
using System.Text.Json;
using KDesktop.Core.Input;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

public sealed partial class WebServer
{
    /// <summary>GET: volume, mute and playback devices.</summary>
    private static async Task AudioStateAsync(HttpListenerResponse response) =>
        await WriteJsonAsync(response, await Task.Run(AudioState).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// POST {volume} | {muted} | {device} | {media: "playPause" | "next" | "previous"}: applies it and answers with
    /// the new state.
    /// </summary>
    private static async Task AudioChangeAsync(HttpListenerContext context)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var state = await Task.Run(() =>
        {
            if (body is not { } b) return AudioState();
            try
            {
                if (b.TryGetProperty("volume", out var v) && v.TryGetInt32(out var volume)) AudioTools.SetVolume(volume);
                if (b.TryGetProperty("muted", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False) AudioTools.SetMute(m.GetBoolean());
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
            }
            catch (Exception ex)
            {
                Log.Warn("Web: error cambiando el audio: " + ex.Message);
            }
            return AudioState();
        }).ConfigureAwait(false);
        await WriteJsonAsync(context.Response, state).ConfigureAwait(false);
    }

    private static object AudioState()
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
        };
    }
}
