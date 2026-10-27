using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using KDesktop.Core.Localization;
using static KDesktop.Core.Assistant.Pipe.PipeSecurityNative;

namespace KDesktop.Core.Assistant.Sharing;

/// <summary>
/// The assistant's tools for other programs (phase 12): an MCP server for Claude Desktop, VS Code, Open WebUI (mcpo)… The
/// program starts <c>KDesktop.exe --mcp</c> as an ordinary stdio MCP server; that bridge only passes the bytes to a pipe
/// of the assistant process (the user's, medium integrity, this data folder), which serves the tools. The pipe asks for a
/// key first: Settings makes it, the program carries it in <c>KDESKTOP_MCP_KEY</c>. What is not only reading asks on the
/// PC first (<c>ToolRunner</c>, route <see cref="Route"/>).
/// </summary>
public static class McpShare
{
    /// <summary>The variable the bridge reads the key from.</summary>
    public const string KeyVariable = "KDESKTOP_MCP_KEY";

    /// <summary>The route of the calls in the activity (and what makes the runner ask).</summary>
    public const string Route = "shared";

    /// <summary>What may be used from outside: only what reads, or everything (asking first).</summary>
    public const string ScopeRead = "read";
    public const string ScopeAll = "all";

    /// <summary>Everything, without asking (only what is set to always ask still does).</summary>
    public const string ScopeFree = "free";

    /// <summary>The first line the bridge writes: this, a space, the key.</summary>
    private const string Hello = "KDMCP1";
    private const int MaxHello = 200;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long the bridge keeps trying while K-Desktop starts the assistant process.</summary>
    public static readonly TimeSpan ConnectTime = TimeSpan.FromSeconds(20);

    /// <summary><c>KDesktop.AssistantMcp.&lt;session&gt;.&lt;folder&gt;</c>, like the assistant's own pipe.</summary>
    public static string PipeNameFor(string dataDir, int sessionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).TrimEnd('\\').ToLowerInvariant()));
        return $"KDesktop.AssistantMcp.{sessionId}.{Convert.ToHexStringLower(hash)[..8]}";
    }

    public static string PipeName => PipeNameFor(AppPaths.DataDir, Process.GetCurrentProcess().SessionId);

    /// <summary>A new key (Settings → «Cambiar la clave»).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static readonly AsyncLocal<string?> Calling = new();

    /// <summary>The program whose call is being answered on this flow (null: not one from outside).</summary>
    public static string? Caller => Calling.Value;

    /// <summary>Answers a call of <paramref name="client"/> from here on (until disposed): what it runs says so in the history.</summary>
    public static IDisposable Answering(string client)
    {
        var previous = Calling.Value;
        Calling.Value = client;
        return new Leave(previous);
    }

    private sealed class Leave(string? previous) : IDisposable
    {
        public void Dispose() => Calling.Value = previous;
    }

    /// <summary>What the assistant process gets: only the hash of the key.</summary>
    public static string HashOf(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim())));

    /// <summary>Whether <paramref name="key"/> is the one whose hash is <paramref name="hash"/> (in constant time).</summary>
    public static bool Matches(string? key, string hash) =>
        key is { Length: > 0 } && hash.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashOf(key)), Encoding.ASCII.GetBytes(hash));

    /// <summary>
    /// What to paste into the program's MCP settings (the usual <c>mcpServers</c> format): this exe with <c>--mcp</c> and
    /// the key; with another data folder, that folder too.
    /// </summary>
    /// <param name="key">Null: no key is needed (no <c>env</c>).</param>
    public static string ClientConfig(string exePath, string? key, string? dataDir = null)
    {
        var args = new JsonArray("--mcp");
        if (dataDir is not null)
        {
            args.Add("--data");
            args.Add(dataDir);
        }
        var server = new JsonObject
        {
            ["command"] = exePath,
            ["args"] = args,
        };
        if (key is not null) server["env"] = new JsonObject { [KeyVariable] = key };
        var root = new JsonObject { ["mcpServers"] = new JsonObject { ["k-desktop"] = server } };
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// One instance of the pipe (the first one with <paramref name="first"/>: nobody may have it before, so nobody can pose as
    /// the assistant): the user and SYSTEM only, medium integrity, no remote clients, as many at once as connect.
    /// </summary>
    public static NamedPipeServerStream CreatePipe(string name, bool first)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Sin usuario");
        var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;{user})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, SDDL_REVISION_1, out var descriptor, out _))
            throw new IOException("SDDL: " + Marshal.GetLastPInvokeErrorMessage());
        try
        {
            var attributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = descriptor };
            const uint unlimited = 255;
            var handle = CreateNamedPipe(@"\\.\pipe\" + name,
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | (first ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0),
                PIPE_TYPE_BYTE | PIPE_REJECT_REMOTE_CLIENTS, unlimited, 64 * 1024, 64 * 1024, 0, in attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeErrorMessage();
                handle.Dispose();
                throw new IOException(error);
            }
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally { LocalFree(descriptor); }
    }

    /// <summary>
    /// The key the bridge sent on its first line, or null (nothing in time, too long, not ours). Byte by byte: what comes
    /// after the line is already the MCP conversation and must stay in the pipe.
    /// </summary>
    public static async Task<string?> ReadHelloAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HelloTimeout);
        var bytes = new List<byte>();
        var one = new byte[1];
        try
        {
            while (bytes.Count < MaxHello)
            {
                if (await stream.ReadAsync(one, timeout.Token).ConfigureAwait(false) == 0) return null;
                if (one[0] == (byte)'\n') break;
                bytes.Add(one[0]);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        var line = Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
        return line.StartsWith(Hello + " ", StringComparison.Ordinal) ? line[(Hello.Length + 1)..] : null;
    }

    /// <summary>
    /// <c>KDesktop.exe --mcp</c>: connects to the assistant's pipe (waiting while K-Desktop starts it), says the key and passes
    /// the bytes both ways until either side ends. 0 when it ended well; 1 (and why, in <paramref name="errors"/>) when it
    /// could not start.
    /// </summary>
    public static async Task<int> BridgeAsync(Stream input, Stream output, string? key, TextWriter errors, CancellationToken cancellationToken)
    {
        // Without a key it still tries: the user may have said none is needed (if not, the assistant hangs up, said below).
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using (pipe.ConfigureAwait(false))
        {
            var until = DateTime.UtcNow + ConnectTime;
            while (true)
            {
                try
                {
                    await pipe.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException && DateTime.UtcNow < until)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is TimeoutException or IOException)
                {
                    await errors.WriteLineAsync("K-Desktop: " + Loc.T("mcpShare.bridge.noPipe")).ConfigureAwait(false);
                    return 1;
                }
            }
            await pipe.WriteAsync(Encoding.ASCII.GetBytes($"{Hello} {key?.Trim()}\n"), cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var up = CopyAsync(input, pipe, done.Token);
            var down = CopyAsync(pipe, output, done.Token);
            var first = await Task.WhenAny(up, down).ConfigureAwait(false);
            await done.CancelAsync().ConfigureAwait(false);
            // The assistant hung up before saying anything: it did not take the key.
            if (first == down && await down.ConfigureAwait(false) == 0)
            {
                await errors.WriteLineAsync("K-Desktop: " + (string.IsNullOrWhiteSpace(key) ? Loc.T("mcpShare.bridge.noKey", KeyVariable) : Loc.T("mcpShare.bridge.badKey")))
                    .ConfigureAwait(false);
                return 1;
            }
            return 0;
        }
    }

    /// <summary>Passes the bytes until <paramref name="from"/> ends; how many went.</summary>
    private static async Task<long> CopyAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                await to.FlushAsync(cancellationToken).ConfigureAwait(false);
                total += read;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        return total;
    }
}
