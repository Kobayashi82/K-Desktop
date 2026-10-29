using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <param name="Id">Item of shell:AppsFolder: an app id (Store apps) or a path relative to a known folder (programs).</param>
public sealed record InstalledApp(string Name, string Id)
{
    /// <summary>What the shell understands to open it or to draw its icon.</summary>
    public string ParsingName => @"shell:AppsFolder\" + Id;

    /// <summary>
    /// Its name inside Windows, in words, for the launcher: the Start menu shows «Bloc de notas» but it is
    /// «Microsoft.WindowsNotepad_8wekyb3d8bbwe!App» (→ «Windows Notepad») or «…\notepad.exe» (→ «notepad»). Null when
    /// there is nothing readable (an id that is a hash) or it is the same as the name.
    /// </summary>
    public string? InnerName
    {
        get
        {
            var id = Id;
            string raw;
            if (id.Contains('!', StringComparison.Ordinal))
            {
                // Store app: the package name without its publisher hash nor its vendor («Microsoft.»).
                var package = id[..id.IndexOf('!', StringComparison.Ordinal)];
                var underscore = package.IndexOf('_', StringComparison.Ordinal);
                if (underscore > 0) package = package[..underscore];
                raw = package[(package.LastIndexOf('.') + 1)..];
            }
            else raw = System.IO.Path.GetFileNameWithoutExtension(id.TrimEnd('\\', '/'));
            if (raw.Length < 3 || raw.All(c => char.IsAsciiHexDigit(c) || c is '-' or '{' or '}')) return null;
            var words = new System.Text.StringBuilder(raw.Length + 4);
            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (c is '.' or '_' or '-') { words.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && char.IsLower(raw[i - 1])) words.Append(' ');
                words.Append(c);
            }
            var text = words.ToString().Trim();
            return text.Length == 0 || string.Equals(text, Name, StringComparison.OrdinalIgnoreCase) ? null : text;
        }
    }
}

/// <summary>
/// The programs of the Start menu, read from the shell's "Applications" folder (shell:AppsFolder), which lists both
/// classic programs and Store apps. Read on a background STA thread and refreshed at most every few minutes. Used by the
/// launcher and by the assistant's «open an app» tool (in the assistant process).
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

    /// <summary>The list, read now if it is empty or old (waits for it).</summary>
    public static async Task<IReadOnlyList<InstalledApp>> GetAsync()
    {
        if (_apps.Count > 0 && DateTime.UtcNow - _loadedUtc < MaxAge) return _apps;
        var done = new TaskCompletionSource<IReadOnlyList<InstalledApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                _apps = Read();
                _loadedUtc = DateTime.UtcNow;
                done.TrySetResult(_apps);
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo leer la lista de programas: " + ex.Message);
                done.TrySetResult(_apps);
            }
        }) { IsBackground = true, Name = "K-Desktop apps" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await done.Task.ConfigureAwait(false);
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
