using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Tools.Declarative;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KDesktop.Assistant.Tools.Mcp;

/// <summary>How an MCP server is.</summary>
public enum McpRunState { Stopped, Starting, Running, Failed }

/// <summary>A server that could not start or answer, with why in the user's language.</summary>
public sealed class McpServerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One MCP server (stage 9.2): started when one of its tools is needed and stopped after <see cref="McpServerEntry.Idle"/>
/// unused. A program is started here, as the normal user and in <see cref="ChildJob"/> (it dies with the assistant
/// process), and spoken to through <c>StreamClientTransport</c>; what it writes to stdout that is not a JSON line is
/// left out (some servers write their traces there) and its stderr goes to <c>Logs\mcp-&lt;id&gt;.log</c>. An address
/// is spoken to with the SDK's HTTP transport. One start at a time; thread-safe.
/// </summary>
public sealed class McpServer : IAsyncDisposable
{
    /// <summary>The longest a start may take (npx and uvx download the server the first time).</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);

    /// <summary>How much of its log is kept when it starts again.</summary>
    private const long MaxLog = 512 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _logGate = new();
    private readonly ToolSecrets? _secrets;
    private readonly HttpClient _http;
    private readonly string _logFile;
    private readonly Queue<string> _lastErrors = new();
    private readonly Timer _idle;
    private McpClient? _client;
    private Process? _process;
    private long _lastUse;
    private int _busy;

    public McpServer(McpServerEntry entry, ToolSecrets? secrets, HttpClient http, string logFolder)
    {
        Entry = entry;
        _secrets = secrets;
        _http = http;
        _logFile = Path.Combine(logFolder, $"mcp-{entry.Id}.log");
        _idle = new Timer(_ => _ = StopIfIdleAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>Its entry (a new one with the same <see cref="McpServerEntry.Key"/> replaces it: timeouts, switched off).</summary>
    public McpServerEntry Entry { get; set; }

    public string Id => Entry.Id;

    public McpRunState State { get; private set; }

    /// <summary>Why it failed, in the user's language.</summary>
    public string? Error { get; private set; }

    /// <summary>Since when it is in <see cref="State"/>.</summary>
    public DateTimeOffset? Since { get; private set; }

    /// <summary>It changed state.</summary>
    public event Action<McpServer>? Changed;

    /// <summary>It started and said its tools (each start: they may have changed).</summary>
    public event Action<McpServer, IList<McpClientTool>>? Listed;

    /// <summary>Its tools now (starting it if it must).</summary>
    public async Task<IList<McpClientTool>> ListAsync(CancellationToken cancellationToken)
    {
        // Busy before it waits for a start: a stop for being unused must not cut it.
        Interlocked.Increment(ref _busy);
        try
        {
            var client = await ClientAsync(cancellationToken).ConfigureAwait(false);
            return await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (Lost(ex, cancellationToken)) { throw new McpServerException(Loc.T("tools.mcp.lost", Id, Mask(ex.Message)), ex); }
        finally { Done(); }
    }

    /// <summary>
    /// A failure of the connection (the process went, the network): said as the server's; not the server's own errors
    /// (<c>McpException</c>), ours, or the caller giving up.
    /// </summary>
    private static bool Lost(Exception ex, CancellationToken cancellationToken) =>
        ex is not (McpServerException or ModelContextProtocol.McpException or OutOfMemoryException)
        && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested);

    /// <summary>Calls <paramref name="tool"/> (starting it if it must), within <see cref="McpServerEntry.CallTimeout"/>.</summary>
    public async Task<CallToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _busy);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var client = await ClientAsync(cancellationToken).ConfigureAwait(false);
            limit.CancelAfter(Entry.CallTimeout);
            return await client.CallToolAsync(tool, arguments, cancellationToken: limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new McpServerException(Loc.T("tools.mcp.slow", Id, (int)Entry.CallTimeout.TotalSeconds));
        }
        catch (Exception ex) when (Lost(ex, cancellationToken)) { throw new McpServerException(Loc.T("tools.mcp.lost", Id, Mask(ex.Message)), ex); }
        finally { Done(); }
    }

    private void Done()
    {
        Interlocked.Decrement(ref _busy);
        Interlocked.Exchange(ref _lastUse, Environment.TickCount64);
    }

    private async Task<McpClient> ClientAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null && _process is not { HasExited: true })
            {
                Interlocked.Exchange(ref _lastUse, Environment.TickCount64);
                return _client;
            }
            if (_client is not null) Log.Warn($"Asistente: el servidor MCP {Id} se había cerrado: arranca otra vez");
            await StopCoreAsync().ConfigureAwait(false);
            Set(McpRunState.Starting, null);
            var started = Stopwatch.StartNew();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(StartTimeout);
            try
            {
                _client = await ConnectAsync(limit.Token).ConfigureAwait(false);
                var tools = await _client.ListToolsAsync(cancellationToken: limit.Token).ConfigureAwait(false);
                Interlocked.Exchange(ref _lastUse, Environment.TickCount64);
                Log.Info($"Asistente: servidor MCP {Id} en marcha en {started.ElapsedMilliseconds} ms ({tools.Count} herramienta(s)"
                    + (_process is { } p ? $", pid {p.Id})" : ")"));
                Set(McpRunState.Running, null);
                Listed?.Invoke(this, tools);
                return _client;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var why = ex switch
                {
                    MissingProgramException missing => Loc.T("tools.mcp.noProgram", missing.Program),
                    OperationCanceledException => Loc.T("tools.mcp.noStart", (int)StartTimeout.TotalSeconds),
                    _ => ex.Message,
                };
                // What it said before dying says more than «the transport was closed».
                string[] said;
                lock (_logGate) said = [.. _lastErrors];
                if (said.Length > 0 && ex is not MissingProgramException) why += " · " + said[^1];
                Log.Warn($"Asistente: el servidor MCP {Id} no arranca: {Mask(why)}");
                await StopCoreAsync().ConfigureAwait(false);
                Set(McpRunState.Failed, Mask(why));
                throw new McpServerException(Mask(why), ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var template = new JsonTemplate(new Dictionary<string, JsonNode?>(), _secrets, withSecrets: true);
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "K-Desktop", Version = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "5" },
            InitializationTimeout = StartTimeout,
        };
        IClientTransport transport;
        if (Entry.Url is { } url)
        {
            transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(template.Fill(url)),
                TransportMode = Entry.Transport == "sse" ? HttpTransportMode.Sse : HttpTransportMode.AutoDetect,
                AdditionalHeaders = Entry.Headers.ToDictionary(h => h.Key, h => template.Fill(h.Value)),
                Name = Id,
            }, _http, loggerFactory: null, ownsHttpClient: false);
        }
        else
        {
            transport = StartProcess(template);
        }
        return await McpClient.CreateAsync(transport, options, loggerFactory: null, cancellationToken).ConfigureAwait(false);
    }

    private StreamClientTransport StartProcess(JsonTemplate template)
    {
        var arguments = Entry.Args.Select(a => template.Fill(a)).ToList();
        var program = Resolve(template.Fill(Entry.Command!));
        ProcessStartInfo start;
        if (Path.GetExtension(program) is ".cmd" or ".bat")
        {
            // npx.cmd, uvx.cmd…: through cmd, each argument quoted (a quote inside one would end it).
            if (arguments.FirstOrDefault(a => a.Contains('"', StringComparison.Ordinal)) is { } bad)
                throw new ArgumentException($"El argumento «{bad}» lleva comillas: cmd no lo puede recibir");
            start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = "/d /s /c \"\"" + program + "\"" + string.Concat(arguments.Select(a => " \"" + a + "\"")) + "\"",
            };
        }
        else
        {
            start = new ProcessStartInfo(program);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
        }
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = new UTF8Encoding(false);
        start.StandardErrorEncoding = new UTF8Encoding(false);
        start.WorkingDirectory = Entry.Cwd is { Length: > 0 } cwd ? template.Fill(cwd)
            : arguments.Where(a => Path.IsPathFullyQualified(a) && File.Exists(a)).Select(Path.GetDirectoryName).FirstOrDefault()
              ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (name, value) in Entry.Env) start.Environment[name] = template.Fill(value);
        // Python servers: their text in UTF-8 and without waiting to fill a buffer.
        start.Environment.TryAdd("PYTHONIOENCODING", "utf-8");
        start.Environment.TryAdd("PYTHONUNBUFFERED", "1");

        TrimLog();
        WriteLog($"── {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} arranca: {Mask(start.FileName)} {Mask(start.Arguments.Length > 0 ? start.Arguments : string.Join(' ', start.ArgumentList))}");
        lock (_logGate) _lastErrors.Clear();
        var process = Process.Start(start) ?? throw new McpServerException(Loc.T("tools.mcp.noStart", 0));
        ChildJob.Add(process, $"servidor MCP {Id}");
        _process = process;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            WriteLog($"── se ha cerrado (código {SafeExitCode(process)})");
            if (ReferenceEquals(_process, process) && State == McpRunState.Running) Set(McpRunState.Stopped, null);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { Length: > 0 } line) return;
            WriteLog(Mask(line));
            lock (_logGate)
            {
                _lastErrors.Enqueue(Mask(line.Length > 300 ? line[..300] : line));
                while (_lastErrors.Count > 5) _lastErrors.Dequeue();
            }
        };
        process.BeginErrorReadLine();

        // Only JSON lines reach the SDK: mcp-communicator-telegram writes «Received request: …» to stdout.
        var pipe = new Pipe();
        _ = Task.Run(async () =>
        {
            var output = pipe.Writer.AsStream();
            try
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.TrimStart().StartsWith('{'))
                    {
                        await output.WriteAsync(Encoding.UTF8.GetBytes(line + "\n")).ConfigureAwait(false);
                        await output.FlushAsync().ConfigureAwait(false);
                    }
                    else if (line.Trim().Length > 0) WriteLog("stdout: " + Mask(line));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
            finally { await pipe.Writer.CompleteAsync().ConfigureAwait(false); }
        });
        return new StreamClientTransport(process.StandardInput.BaseStream, pipe.Reader.AsStream(), loggerFactory: null);
    }

    /// <summary>The program to start: a full path as it is, else found on the PATH (npx → npx.cmd).</summary>
    private static string Resolve(string command)
    {
        if (Path.IsPathFullyQualified(command))
            return File.Exists(command) ? command : throw new MissingProgramException(command);
        return ScriptProcess.Find(command)
            ?? (Path.HasExtension(command) ? ScriptProcess.Find(Path.GetFileNameWithoutExtension(command)) : null)
            ?? throw new MissingProgramException(ProgramName(command));
    }

    /// <summary>What to install for <paramref name="command"/>: npx → Node.js, uvx → uv…</summary>
    public static string ProgramName(string command) => Path.GetFileNameWithoutExtension(command).ToLowerInvariant() switch
    {
        "node" or "npx" or "npm" => "Node.js",
        "uv" or "uvx" => "uv",
        "python" or "python3" or "py" => "Python",
        "docker" => "Docker",
        var other => other,
    };

    /// <summary>Stops it unless something is using it or waiting for it (after starting it only to learn its tools).</summary>
    public async Task StopIfUnusedAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _busy) > 0 || (_client is null && _process is null)) return;
            await StopCoreAsync().ConfigureAwait(false);
            Set(McpRunState.Stopped, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops it now (Options, or a change of its entry).</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is null && _process is null) return;
            await StopCoreAsync().ConfigureAwait(false);
            Set(McpRunState.Stopped, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopIfIdleAsync()
    {
        if (State != McpRunState.Running || Volatile.Read(ref _busy) > 0) return;
        if (Environment.TickCount64 - Interlocked.Read(ref _lastUse) < Entry.Idle.TotalMilliseconds) return;
        Log.Info($"Asistente: servidor MCP {Id} parado: {Entry.Idle.TotalMinutes:0.#} min sin usarse");
        await StopIfUnusedAsync().ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        var client = _client;
        var process = _process;
        _client = null;
        _process = null;
        if (client is not null)
        {
            try { await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        if (process is not null)
        {
            try
            {
                if (!process.HasExited && !process.WaitForExit(1500)) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
    }

    private void Set(McpRunState state, string? error)
    {
        State = state;
        Error = error;
        Since = DateTimeOffset.Now;
        Changed?.Invoke(this);
    }

    private string Mask(string text) => _secrets?.Mask(text) ?? text;

    private static string SafeExitCode(Process process)
    {
        try { return process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch (InvalidOperationException) { return "?"; }
    }

    private void WriteLog(string line)
    {
        lock (_logGate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Its log keeps only the last part when it grows too much.</summary>
    private void TrimLog()
    {
        lock (_logGate)
        {
            try
            {
                if (!File.Exists(_logFile) || new FileInfo(_logFile).Length <= MaxLog) return;
                var text = File.ReadAllText(_logFile);
                File.WriteAllText(_logFile, text[^(int)(MaxLog / 2)..]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _idle.DisposeAsync().ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
