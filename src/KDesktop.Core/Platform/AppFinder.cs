using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Native;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Platform;

/// <summary>A window of a program, as <see cref="AppFinder"/> sees it: its process, its title and what its exe calls itself.</summary>
/// <param name="Process">Its process's name, lower case, without «.exe».</param>
/// <param name="Description">The exe's own name for itself («Google Chrome»), when it has one.</param>
public sealed record OpenWindow(IntPtr Hwnd, uint Pid, string Process, string Title, string? Description)
{
    /// <summary>The name to say to the user: what the exe calls itself, or its process.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Process : Description.Trim();
}

/// <summary>
/// Finds the program an order names («minimiza Steam», «sal de Chrome», «abre el bloc de notas») among the open windows
/// or the installed apps. Word by word: every word said must be one of the name's words (or sound like it, or be its
/// initials: «vs code»); the name may have more («chrome» is "Google Chrome"). Not a launcher's subsequence search,
/// which took «Steam» for "Inno Setup Example Scripts". Generic words («el programa», «la ventana de») do not count:
/// <c>assistant.act.generic</c>, any language.
/// </summary>
public static class AppFinder
{
    /// <summary>A name this close is the one meant.</summary>
    public const double Min = 0.8;

    /// <summary>How close a word said must be to one of the name's.</summary>
    private const double WordMin = 0.75;

    /// <summary>How well <paramref name="said"/> names <paramref name="name"/>, 0 to 1.</summary>
    public static double Score(string said, string name)
    {
        var spoken = Spoken(said);
        var named = TextSimilarity.Words(name).Where(w => !Filler.Value.Contains(w)).ToArray();
        if (spoken.Length == 0 || named.Length == 0) return 0;
        // Said or written together: «vscode» / "VS Code", «you tube» / "YouTube".
        var joined = TextSimilarity.Ratio(string.Concat(spoken), string.Concat(named));
        var together = joined >= 0.85 ? joined : 0;
        var used = new bool[named.Length];
        double total = 0;
        foreach (var word in spoken)
        {
            var (best, from, count) = BestFor(word, named);
            if (best < WordMin) return together;
            total += best;
            for (var i = from; i < from + count; i++) used[i] = true;
        }
        var coverage = (double)used.Count(u => u) / named.Length;
        return Math.Max(total / spoken.Length * (0.85 + 0.15 * coverage), together);
    }

    /// <summary>The best of the name's words for one said (or two of them joined, or several by their initials).</summary>
    private static (double Score, int From, int Count) BestFor(string word, string[] named)
    {
        (double Score, int From, int Count) best = (0, 0, 1);
        for (var i = 0; i < named.Length; i++)
        {
            var s = Math.Max(TextSimilarity.WordSimilarity(named[i], word), Sounds(named[i], word));
            if (s > best.Score) best = (s, i, 1);
            if (i + 1 < named.Length)
            {
                var pair = TextSimilarity.Ratio(named[i] + named[i + 1], word);
                if (pair > best.Score) best = (pair, i, 2);
            }
            // «vs» for "Visual Studio", and «vscode» for "Visual Studio Code" (the initials and the last word).
            for (var n = 2; n <= Math.Min(4, named.Length - i); n++)
            {
                var initials = string.Concat(named.Skip(i).Take(n).Select(w => w[0]));
                if (word == initials && best.Score < 1) best = (1, i, n);
                if (i + n < named.Length && word == initials + named[i + n] && best.Score < 1) best = (1, i, n + 1);
            }
        }
        return best;
    }

    private static double Sounds(string written, string said) =>
        written.Length < 4 || said.Length < 4 ? 0 : 0.95 * PhoneticKey.Similarity(PhoneticKey.Encode(written), PhoneticKey.Encode(said));

    /// <summary>The words of an order that name the program: without fillers and generic words.</summary>
    private static string[] Spoken(string said) =>
        [.. TextSimilarity.Words(said).Where(w => !Filler.Value.Contains(w) && !Generic.Value.Contains(w))];

    private static readonly Lazy<HashSet<string>> Filler = new(() => VoiceVocabulary.Current.Filler);

    private static readonly Lazy<HashSet<string>> Generic = new(() =>
    [
        .. Loc.Available().SelectMany(l => TextSimilarity.Words(Loc.Strings(l.Code, "assistant.act.generic").GetValueOrDefault("assistant.act.generic") ?? string.Empty)),
    ]);

    /// <summary>Whether an order names a program at all (not only generic words: «la ventana»).</summary>
    public static bool NamesSomething(string said) => Spoken(said).Length > 0;

    /// <summary>The best of <paramref name="items"/> by any of its names, if it is at least <see cref="Min"/>; the first wins a tie.</summary>
    public static T? Best<T>(string said, IEnumerable<T> items, Func<T, IEnumerable<string?>> names) where T : class =>
        items.Select(item => (Item: item, Score: names(item).OfType<string>().Select(n => Score(said, n)).DefaultIfEmpty(0).Max()))
            .Where(p => p.Score >= Min)
            .OrderByDescending(p => p.Score)
            .Select(p => p.Item)
            .FirstOrDefault();

    /// <summary>The open window an order names (the one most in front when several fit), or null.</summary>
    public static OpenWindow? FindWindow(string said, IReadOnlyList<OpenWindow> windows) =>
        Best(said, windows, w => [w.Process, w.Description, w.Title]);

    /// <summary>The installed app an order names, or null (the shortest name wins a tie: "Steam", not "Steam VR").</summary>
    public static InstalledApp? FindApp(string said, IEnumerable<InstalledApp> apps) =>
        Best(said, apps.OrderBy(a => a.Name.Length), a => [a.Name]);

    /// <summary>A running program by its process name, with or without windows («sal de Steam» with Steam in the tray).</summary>
    public static string? FindProcess(string said)
    {
        var names = Process.GetProcesses().Select(p =>
        {
            using (p) return p.ProcessName.ToLowerInvariant();
        }).Distinct().ToList();
        return Best(said, names, n => [n]);
    }

    private static readonly ConcurrentDictionary<string, string?> Descriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The windows of programs on the desktop, the one in front first: visible, not owned, with a title, not tool windows
    /// nor hidden ones (cloaked: other virtual desktops, suspended Store apps), not K-Desktop's nor the desktop.
    /// </summary>
    public static List<OpenWindow> OpenWindows()
    {
        var result = new List<OpenWindow>();
        var own = (uint)Environment.ProcessId;
        var shell = Win32.GetShellWindow();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == shell || !Win32.IsWindowVisible(hwnd) || Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) return true;
            if (((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0 || IsCloaked(hwnd)) return true;
            var title = Win32.GetWindowTitle(hwnd);
            if (title.Length == 0) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == own) return true;
            var process = ProcessNames.Get(pid);
            if (process.Length == 0) return true;
            result.Add(new OpenWindow(hwnd, pid, process, title, DescriptionOf(pid)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string? DescriptionOf(uint pid)
    {
        if (Win32.GetProcessPath(pid) is not { } path) return null;
        return Descriptions.GetOrAdd(path, p =>
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(p);
                return string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        });
    }

    private static bool IsCloaked(IntPtr hwnd) => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private const uint DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
}
