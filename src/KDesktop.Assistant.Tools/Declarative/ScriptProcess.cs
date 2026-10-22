using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KDesktop.Assistant.Tools.Declarative;

/// <summary>A program a script needs that is not on this PC (node, python…).</summary>
public sealed class MissingProgramException(string program) : Exception($"No se encuentra {program}")
{
    public string Program => program;
}

/// <param name="Exit">Its exit code.</param>
/// <param name="Output">What it wrote (stdout), cut.</param>
/// <param name="Error">What it wrote to stderr, cut.</param>
public sealed record ScriptOutcome(int Exit, string Output, string Error);

/// <summary>
/// Runs a script tool (stage 9.1) as the normal user, without a window: <c>.ps1</c> with Windows PowerShell, <c>.bat</c>
/// and <c>.cmd</c> with cmd, <c>.py</c> with uv (it reads the dependencies the script declares) or else Python,
/// <c>.js</c> with Node, and <c>.exe</c> as it is. Arguments go one by one (never through a shell), except to cmd,
/// which reads them again: there, characters that would make it do something else are refused.
/// </summary>
public static partial class ScriptProcess
{
    /// <summary>How much of what it writes is kept.</summary>
    public const int MaxOutput = 64 * 1024;

    private static readonly System.Buffers.SearchValues<char> CmdSpecial = System.Buffers.SearchValues.Create("\"&|<>^%!\r\n");

    public static async Task<ScriptOutcome> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var start = StartInfo(file, arguments, out var encoding);
        start.WorkingDirectory = Path.GetDirectoryName(file)!;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = encoding;
        start.StandardErrorEncoding = encoding;
        foreach (var (name, value) in environment) start.Environment[name] = value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"No se ha podido arrancar {start.FileName}");
        process.StandardInput.Close();
        var output = ReadAsync(process.StandardOutput);
        var error = ReadAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            throw;
        }
        return new ScriptOutcome(process.ExitCode, (await output.ConfigureAwait(false)).Trim(), (await error.ConfigureAwait(false)).Trim());
    }

    private static ProcessStartInfo StartInfo(string file, IReadOnlyList<string> arguments, out Encoding encoding)
    {
        encoding = new UTF8Encoding(false);
        ProcessStartInfo start;
        switch (Path.GetExtension(file).ToLowerInvariant())
        {
            case ".ps1":
                // Windows PowerShell writes in the console's code page when its output is redirected.
                encoding = Oem();
                start = With(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                    ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file, .. arguments]);
                break;
            case ".bat" or ".cmd":
                if (arguments.FirstOrDefault(a => a.IndexOfAny(CmdSpecial) >= 0) is { } bad)
                    throw new ArgumentException($"El argumento «{bad}» lleva caracteres que un .bat no puede recibir");
                encoding = Oem();
                start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                {
                    Arguments = "/d /s /c \"\"" + file + "\"" + string.Concat(arguments.Select(a => " \"" + a + "\"")) + "\"",
                };
                break;
            case ".py":
                if (Find("uv") is { } uv) start = With(uv, ["run", "--quiet", "--script", file, .. arguments]);
                else if (Find("py") is { } py) start = With(py, ["-3", file, .. arguments]);
                else start = With(Find("python") ?? throw new MissingProgramException("Python (o uv)"), [file, .. arguments]);
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                start.Environment["PYTHONUTF8"] = "1";
                break;
            case ".js":
                start = With(Find("node") ?? throw new MissingProgramException("Node.js"), [file, .. arguments]);
                break;
            default:
                start = With(file, arguments);
                break;
        }
        return start;
    }

    private static ProcessStartInfo With(string program, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(program);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>A program on the PATH (with the extensions of PATHEXT), or null. The Store's «python» that only opens the Store does not count.</summary>
    public static string? Find(string name)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var extension in extensions)
            {
                try
                {
                    var path = Path.Combine(folder, name + extension);
                    if (File.Exists(path)) return path;
                }
                catch (ArgumentException) { break; }
            }
        }
        return null;
    }

    private static async Task<string> ReadAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            if (text.Length < MaxOutput) text.Append(buffer, 0, Math.Min(read, MaxOutput - text.Length));
        return text.ToString();
    }

    private static Encoding Oem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding((int)GetOEMCP()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.Latin1; }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();
}
