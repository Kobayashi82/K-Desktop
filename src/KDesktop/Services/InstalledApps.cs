using System.IO;
using System.Runtime.InteropServices;
using KDesktop.Core;
using KDesktop.Core.Platform;

namespace KDesktop.Services;

/// <param name="Id">Item of shell:AppsFolder: an app id (Store apps) or a path relative to a known folder (programs).</param>
public sealed record InstalledApp(string Name, string Id)
{
    /// <summary>What the shell understands to open it or to draw its icon.</summary>
    public string ParsingName => @"shell:AppsFolder\" + Id;
}

/// <summary>
/// The programs of the Start menu, read from the shell's "Applications" folder (shell:AppsFolder), which lists both
/// classic programs and Store apps. Read on a background STA thread and refreshed at most every few minutes.
/// </summary>
public static class InstalledApps
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static readonly string[] SkippedExtensions = [".url", ".txt", ".chm", ".pdf", ".htm", ".html", ".rtf", ".md", ".log"];
    private static readonly string[] SkippedWords = ["uninstall", "desinstalar", "unins000"];

    private static IReadOnlyList<InstalledApp> _apps = [];
    private static DateTime _loadedUtc = DateTime.MinValue;
    private static int _loading;

    public static IReadOnlyList<InstalledApp> Current => _apps;

    /// <summary>Raised (on a worker thread) when the list has been read again.</summary>
    public static event Action? Updated;

    public static void Refresh()
    {
        if (DateTime.UtcNow - _loadedUtc < MaxAge || Interlocked.Exchange(ref _loading, 1) == 1) return;
        var thread = new Thread(() =>
        {
            try
            {
                _apps = Read();
                _loadedUtc = DateTime.UtcNow;
                Updated?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo leer la lista de programas: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _loading, 0);
            }
        }) { IsBackground = true, Name = "K-Desktop apps" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static bool Launch(InstalledApp app) =>
        ProcessLauncher.Launch(new LaunchSpec("explorer.exe", $"\"{app.ParsingName}\""), asAdmin: false);

    private static List<InstalledApp> Read()
    {
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            var result = new List<InstalledApp>();
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            dynamic folder = shell.NameSpace(@"shell:AppsFolder");
            foreach (var item in folder.Items())
            {
                string name = item.Name ?? "";
                string id = item.Path ?? "";
                if (name.Length == 0 || id.Length == 0 || id.Contains("://", StringComparison.Ordinal)) continue;
                if (SkippedExtensions.Contains(Path.GetExtension(id), StringComparer.OrdinalIgnoreCase)) continue;
                if (SkippedWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase) || id.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                if (names.Add(name)) result.Add(new InstalledApp(name, id));
            }
            return result;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
