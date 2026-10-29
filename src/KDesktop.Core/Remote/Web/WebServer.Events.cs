using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace KDesktop.Core.Remote.Web;

public sealed partial class WebServer
{
    private const int MaxEventClients = 32;
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(25);

    /// <summary>An open "server-sent events" stream: one per open web app, of the device it belongs to.</summary>
    private sealed record EventClient(SemaphoreSlim Gate, string DeviceId);

    private readonly ConcurrentDictionary<HttpListenerResponse, EventClient> _eventClients = new();

    /// <summary>Tells the open web apps that the commands or menus changed (they reload the catalog).</summary>
    public void NotifyCatalogChanged() => Broadcast("catalog");

    /// <summary>An event with no data, to every open app.</summary>
    private void Broadcast(string name) => Send(name, null, null);

    /// <summary>An event with its data (JSON), to every open app.</summary>
    private void Broadcast(string name, object data) => Send(name, data, null);

    /// <summary>An event only for the apps open on one device (what that phone asked for).</summary>
    internal void SendTo(string deviceId, string name, object? data) => Send(name, data, deviceId);

    private void Send(string name, object? data, string? deviceId)
    {
        if (_eventClients.IsEmpty) return;
        var json = data is null ? "{}" : JsonSerializer.Serialize(data, WebJson.Options);
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {json}\n\n");
        foreach (var (response, client) in _eventClients)
            if (deviceId is null || client.DeviceId == deviceId) _ = WriteEventAsync(response, client.Gate, bytes);
    }

    /// <summary>Whether a device has the app open now (its stream).</summary>
    private bool Listening(string deviceId) => _eventClients.Values.Any(c => c.DeviceId == deviceId);

    private async Task WriteEventAsync(HttpListenerResponse response, SemaphoreSlim gate, byte[] bytes)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            await response.OutputStream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Gone: the stream loop notices and cleans up.
            _eventClients.TryRemove(response, out _);
        }
        finally { gate.Release(); }
    }

    /// <summary>GET api/events: keeps the response open and writes events (and a ping now and then).</summary>
    private async Task EventsAsync(HttpListenerResponse response, WebDevice device)
    {
        if (_eventClients.Count >= MaxEventClients)
        {
            await WriteJsonAsync(response, new { error = "busy" }, 503).ConfigureAwait(false);
            return;
        }
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Accel-Buffering"] = "no";
        response.SendChunked = true;

        var gate = new SemaphoreSlim(1, 1);
        _eventClients[response] = new EventClient(gate, device.Id);
        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            await WriteEventAsync(response, gate, Encoding.UTF8.GetBytes("retry: 5000\n\n")).ConfigureAwait(false);
            while (!token.IsCancellationRequested && _eventClients.ContainsKey(response))
            {
                await Task.Delay(KeepAlive, token).ConfigureAwait(false);
                await WriteEventAsync(response, gate, ": ping\n\n"u8.ToArray()).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _eventClients.TryRemove(response, out _);
        }
    }
}
