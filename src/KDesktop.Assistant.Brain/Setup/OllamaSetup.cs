using System.ComponentModel;
using System.Diagnostics;
using KDesktop.Core;

namespace KDesktop.Assistant.Brain.Setup;

/// <summary>
/// Ollama on this PC: whether it is installed, opening it, and installing it with winget (or opening its download page
/// when there is no winget). It runs in the assistant process, so Ollama is installed for the user, never «as administrator».
/// </summary>
public static class OllamaSetup
{
    public const string DownloadPage = "https://ollama.com/download/windows";

    private const string WingetId = "Ollama.Ollama";

    private static string LocalData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Ollama's program (the one in the tray, which starts the server).</summary>
    public static string AppPath => Path.Combine(LocalData, "Programs", "Ollama", "ollama app.exe");

    private static string WingetPath => Path.Combine(LocalData, "Microsoft", "WindowsApps", "winget.exe");

    public static bool IsInstalled => File.Exists(AppPath);

    /// <summary>Whether it can be installed from here (winget comes with Windows 10 and 11 when up to date).</summary>
    public static bool CanInstall => File.Exists(WingetPath);

    /// <summary>Opens Ollama; false when it could not.</summary>
    public static bool Start()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(AppPath) });
            Log.Info("Asistente: Ollama abierto");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn($"Asistente: no se ha podido abrir Ollama: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Installs Ollama with winget, without windows. Null when it went well, or why not. Cancelling stops waiting, but the
    /// installation goes on: an installer is never cut halfway.
    /// </summary>
    public static async Task<string?> InstallAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(WingetPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in (string[])["install", "--id", WingetId, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"])
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("winget");
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false) + await errors.ConfigureAwait(false);
            if (process.ExitCode == 0 || IsInstalled)
            {
                Log.Info("Asistente: Ollama instalado con winget");
                return null;
            }
            Log.Warn($"Asistente: winget no ha podido instalar Ollama (0x{process.ExitCode:X8}): {LastLine(text)}");
            return $"winget 0x{process.ExitCode:X8}";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Asistente: no se ha podido usar winget: {ex.Message}");
            return ex.Message;
        }
    }

    public static void OpenDownloadPage()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Asistente: no se ha podido abrir la página de Ollama: {ex.Message}");
        }
    }

    /// <summary>The last line with something in it (winget draws progress bars with carriage returns).</summary>
    private static string LastLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => l.Any(char.IsLetter)) ?? string.Empty;
}
