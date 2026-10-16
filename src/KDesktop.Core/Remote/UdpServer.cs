using System.Net;
using System.Net.Sockets;
using System.Text;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Remote;

/// <summary>
/// UDP remote control, wire-compatible with K-Desktop 4 (default port 2501).
/// Text commands: "lista" / "list" / "0", a number, a remote command name, built-ins ("play", "next", …),
/// "sk X" / "sendkey X" and "show kdesktop".
/// </summary>
public sealed class UdpServer : IDisposable
{
    private const int FirstCommandIndex = 20;

    private static readonly (string Name, string[] Aliases, Action<UdpServer> Run)[] BuiltIns =
    [
        ("Play/Pause", ["play", "pause", "play/pause"], s => s.Media(0xB3, releaseTaskSwitch: true)),
        ("Next", ["next"], s => s.Media(0xB0)),
        ("Previous", ["previous"], s => s.Media(0xB1)),
        ("Subir Volumen", ["subir volumen"], s => s.Media(0xAF)),
        ("Bajar Volumen", ["bajar volumen"], s => s.Media(0xAE)),
        ("Silenciar Volumen", ["silenciar volumen"], s => s.Media(0xAD)),
        ("Enter", ["enter"], s => s.Media(0x0D, releaseTaskSwitch: true)),
        ("Close", ["close"], s => { s.EndTaskSwitch(); InputSimulator.SendCombo("ALT + F4"); }),
        ("Up", ["up"], s => InputSimulator.KeyPress(0x26)),
        ("Down", ["down"], s => InputSimulator.KeyPress(0x28)),
        ("Left", ["left"], s => InputSimulator.KeyPress(0x25)),
        ("Right", ["right"], s => InputSimulator.KeyPress(0x27)),
        ("Tasks", ["tasks"], s => s.ToggleTaskSwitch()),
        ("Refresh", ["refresh"], s => InputSimulator.KeyPress(0x74)),
        ("Fullscreen", ["fullscreen"], s => { s.EndTaskSwitch(); InputSimulator.KeyPress('F'); }),
        ("Subs-", ["subs-", "sub-"], s => InputSimulator.KeyPress(0xDB)),
        ("Subs+", ["subs+", "sub+"], s => InputSimulator.KeyPress(0xDD)),
        ("Sync-", ["sync-"], s => InputSimulator.KeyPress('7')),
        ("Sync+", ["sync+"], s => InputSimulator.KeyPress('9')),
    ];

    private readonly CommandEngine _engine;
    private readonly IUiServices _ui;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private bool _taskSwitch;

    public int Port { get; private set; }
    public bool AllowRemote { get; set; }
    public bool IsRunning => _client is not null;

    public UdpServer(CommandEngine engine, IUiServices ui)
    {
        _engine = engine;
        _ui = ui;
    }

    public void Start(int port, bool allowRemote)
    {
        if (IsRunning && port == Port)
        {
            AllowRemote = allowRemote;
            return;
        }
        Stop();
        Port = port;
        AllowRemote = allowRemote;
        try
        {
            var client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _client = client;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(client, _cts.Token));
            Log.Info($"Servidor UDP escuchando en el puerto {port}");
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir el puerto UDP {port}", ex);
            _client = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _client = null;
        _cts = null;
    }

    private async Task ListenAsync(UdpClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await client.ReceiveAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex)
            {
                // ICMP "port unreachable" from a previous reply shows up here; keep listening.
                Log.Warn($"UDP: {ex.SocketErrorCode}");
                continue;
            }

            if (!AllowRemote && !IPAddress.IsLoopback(packet.RemoteEndPoint.Address)) continue;
            var text = Encoding.UTF8.GetString(packet.Buffer).Replace("\r", "").Replace("\n", "").Trim();
            if (text.Length == 0) continue;

            var remote = packet.RemoteEndPoint;
            _ = Task.Run(async () =>
            {
                try
                {
                    var reply = Handle(text);
                    if (reply is not null) await SendAsync(reply, remote).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error($"UDP: error procesando '{text}'", ex);
                }
            }, token);
        }
    }

    private async Task SendAsync(string message, IPEndPoint to)
    {
        if (_client is not { } client) return;
        var data = Encoding.UTF8.GetBytes(message);
        try { await client.SendAsync(data, to).ConfigureAwait(false); }
        catch (Exception ex) { Log.Warn($"UDP: no se pudo responder a {to}: {ex.Message}"); }
    }

    /// <summary>Remote commands in a stable order: their index is 20 + position.</summary>
    private List<Command> RemoteCommands() =>
        _engine.Config.Commands.Where(c => c.Remote)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Processes one request and returns the reply text.</summary>
    internal string? Handle(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower == "show kdesktop")
        {
            _ui.ShowMainWindow();
            return null;
        }

        if (lower is "list" or "lista" or "comandos" or "commands" or "help" or "ayuda" or "0")
            return BuildList(text);

        // By index.
        var digits = text.Replace(" ", "");
        if (digits.Length > 0 && digits.All(char.IsDigit) && int.TryParse(digits, out var index))
        {
            if (index >= 1 && index < FirstCommandIndex)
            {
                var b = BuiltIns[index - 1];
                b.Run(this);
                return $"[{index}] - {b.Name} (done)";
            }
            var remote = RemoteCommands();
            var i = index - FirstCommandIndex;
            if (i < 0 || i >= remote.Count)
                return $"[{index}] - No existe\r\n\r\nEl comando no existe, escriba 'Lista' \r\npara ver los comandos disponibles";
            return RunRemote(remote[i], index);
        }

        // Send keys.
        if (lower.StartsWith("sk ", StringComparison.Ordinal) || lower.StartsWith("sendkey ", StringComparison.Ordinal))
        {
            var keys = text[(text.IndexOf(' ') + 1)..];
            EndTaskSwitch();
            var vk = KeyNames.ToVk(keys);
            if (vk != 0 || Hotkey.Parse(keys).Mods != Modifiers.None) InputSimulator.SendCombo(keys);
            else InputSimulator.SendText(keys);
            return $"SendKey [{keys}] (done)";
        }

        // «Llamada remota» event.
        if (lower.StartsWith("evento ", StringComparison.Ordinal) || lower.StartsWith("event ", StringComparison.Ordinal))
        {
            var name = text[(text.IndexOf(' ') + 1)..].Trim();
            var fired = _engine.RemoteCall?.Invoke(name, "UDP") ?? 0;
            return fired > 0 ? $"Evento [{name}] (done)" : $"Evento [{name}]\r\nNingún evento espera esa llamada";
        }

        // Built-in by name.
        for (var b = 0; b < BuiltIns.Length; b++)
        {
            if (!BuiltIns[b].Aliases.Contains(lower)) continue;
            if (!_engine.Config.Settings.DisabledIn.Contains(ForegroundWindow.Capture().ProcessName) || lower == "tasks")
                BuiltIns[b].Run(this);
            return $"[{b + 1}] - {BuiltIns[b].Name} (done)";
        }

        // Remote command by name.
        var list = RemoteCommands();
        var match = list.FindIndex(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (match >= 0) return RunRemote(list[match], FirstCommandIndex + match);

        return $"{text}\r\nEl comando no existe, escriba 'Lista' \r\npara ver los comandos disponibles";
    }

    private string RunRemote(Command command, int index)
    {
        if (!command.Enabled) return $"[{index}] - {command.Name} (deshabilitado)";
        EndTaskSwitch();
        _ = _engine.RunAsync(command, TriggerSource.Remote);
        return $"[{index}] - {command.Name} (done)";
    }

    private string BuildList(string request)
    {
        var sb = new StringBuilder();
        var remote = RemoteCommands();
        sb.Append($"[{remote.Count}] - {request} (done)");
        sb.Append($"\r\n\r\n---------------[{AppPaths.VersionName.ToUpperInvariant()}] - [{Environment.MachineName}]---------------\r\n");
        sb.Append("\r\n[0] - Comandos, Lista, List");
        for (var i = 0; i < BuiltIns.Length; i++) sb.Append($"\r\n[{i + 1}] - {BuiltIns[i].Name}");
        sb.Append("\r\n\r\nSendKey [Letra], SK [Letra]");
        sb.Append("\r\nEvento [Nombre]");
        sb.Append("\r\n\r\n---------------[COMANDOS]---------------\r\n");
        for (var i = 0; i < remote.Count; i++) sb.Append($"\r\n[{FirstCommandIndex + i}] - {remote[i].Name}");
        return sb.ToString();
    }

    // ── built-in helpers ─────────────────────────────────────────────────

    private void Media(int vk, bool releaseTaskSwitch = false)
    {
        InputSimulator.KeyPress(vk);
        if (releaseTaskSwitch) EndTaskSwitch();
    }

    /// <summary>"Tasks": first press holds ALT and shows the task switcher, the next one releases it.</summary>
    private void ToggleTaskSwitch()
    {
        if (_taskSwitch)
        {
            EndTaskSwitch();
            return;
        }
        InputSimulator.ReleaseModifiers();
        InputSimulator.KeyDown(KeyNames.VK_LMENU);
        InputSimulator.KeyPress(KeyNames.VK_TAB);
        _taskSwitch = true;
    }

    private void EndTaskSwitch()
    {
        if (!_taskSwitch) return;
        InputSimulator.KeyUp(KeyNames.VK_LMENU);
        _taskSwitch = false;
    }

    public void Dispose() => Stop();
}
