using System.Net;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// Power from the phone: switching the PC off, restarting it, putting it to sleep or locking it (<c>POST api/power</c>,
/// only with «Energía» on), and what the app keeps to wake it while it is off (the MACs and broadcast of its cards and,
/// with «Energía», «Encender mediante URL»). <c>POST api/wake</c>: this PC wakes another one of its network (the app
/// asks a PC that is on to wake one that is off).
/// </summary>
public sealed partial class WebServer
{
    /// <summary>The version of this API: the app checks it before using what a newer K-Desktop added.</summary>
    /// <remarks>1: up to 5.0.401. 2: pc, wake data, power, wake, events for one device.</remarks>
    public const int ApiVersion = 2;

    /// <summary>The phone may switch the PC off, restart it… (setting «Energía»).</summary>
    public bool PowerAllowed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Broadcast("session");
        }
    }

    /// <summary>
    /// Does what the phone asked (the device's name for the history). Set by the application, never by the tests: without
    /// it the server answers «unavailable» and nothing is switched off.
    /// </summary>
    public Func<PowerMode, string, Task>? RunPower { get; set; }

    private static readonly Dictionary<string, PowerMode> PowerActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lock"] = PowerMode.Lock,
        ["sleep"] = PowerMode.Sleep,
        ["hibernate"] = PowerMode.Hibernate,
        ["restart"] = PowerMode.Restart,
        ["shutdown"] = PowerMode.Shutdown,
    };

    /// <summary>What the app needs to wake this PC, for the session.</summary>
    private object WakeInfo(HttpListenerContext context)
    {
        var adapters = WakeSetup.Adapters(context.Request.LocalEndPoint?.Address);
        var s = _host.Engine.Config.Settings;
        object? url = null;
        if (PowerAllowed && s.WebWakeUrl.Trim() is { Length: > 0 } address)
        {
            var header = s.WebWakeHeaderName.Trim();
            url = new
            {
                url = address,
                method = s.WebWakeMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "GET" : "POST",
                body = s.WebWakeBody,
                header = header.Length > 0 && s.WebWakeHeaderValue.Length > 0 ? header : null,
                value = header.Length > 0 && s.WebWakeHeaderValue.Length > 0 ? s.WebWakeHeaderValue : null,
            };
        }
        return new { adapters = adapters.Select(a => new { a.Mac, a.Broadcast, a.Wired }), url };
    }

    /// <summary>POST api/power {action: lock | sleep | hibernate | restart | shutdown}.</summary>
    private async Task PowerAsync(HttpListenerContext context, WebDevice device)
    {
        if (!PowerAllowed || RunPower is not { } run)
        {
            await WriteJsonAsync(context.Response, new { error = PowerAllowed ? "unavailable" : "notAllowed" }, PowerAllowed ? 503 : 403).ConfigureAwait(false);
            return;
        }
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        if (!PowerActions.TryGetValue(Str(body, "action") ?? string.Empty, out var mode))
        {
            await WriteJsonAsync(context.Response, new { error = "badRequest" }, 400).ConfigureAwait(false);
            return;
        }
        Log.Info($"Web: «{device.Name}» pide {mode}");
        // Answer first: once it runs, the PC may be gone before the answer leaves.
        await WriteJsonAsync(context.Response, new { ok = true }).ConfigureAwait(false);
        context.Response.Close();
        Broadcast("power", new { action = mode.ToString().ToLowerInvariant() });
        await Task.Delay(400).ConfigureAwait(false);
        await run(mode, device.Name).ConfigureAwait(false);
    }

    /// <summary>What the application gives <see cref="RunPower"/>: the «Energía» action, run like a command from the web.</summary>
    public static async Task RunPowerAsync(CommandEngine engine, PowerMode mode, string deviceName)
    {
        var command = new Command { Name = PowerNames.Of(mode) };
        command.Actions.Add(new PowerAction { Mode = mode, DelaySeconds = 0, ShowOsd = false });
        using (ExecutionHistory.Describe(deviceName))
            await engine.RunAsync(command, TriggerSource.Web).ConfigureAwait(false);
    }

    /// <summary>POST api/wake {mac, broadcast?}: the magic packet from this PC, to wake another one of its network.</summary>
    private static async Task WakeAsync(HttpListenerContext context, WebDevice device)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        if (WakeOnLan.ParseMac(Str(body, "mac")) is not { } mac)
        {
            await WriteJsonAsync(context.Response, new { error = "badRequest" }, 400).ConfigureAwait(false);
            return;
        }
        // Only to a broadcast address of this PC's own networks (never a host on the internet).
        var broadcast = Str(body, "broadcast");
        var own = WakeOnLan.DirectedBroadcasts().Select(b => b.ToString()).ToList();
        if (broadcast is not null && !own.Contains(broadcast)) broadcast = null;
        var sent = 0;
        foreach (var port in new[] { 9, 7 }) sent += await WakeOnLan.SendAsync(mac, broadcast, port).ConfigureAwait(false);
        Log.Info($"Web: «{device.Name}» enciende {WakeOnLan.FormatMac(mac)} desde este PC");
        await WriteJsonAsync(context.Response, new { sent }).ConfigureAwait(false);
    }
}
