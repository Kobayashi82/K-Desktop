using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Engine;

/// <summary>
/// Variables inside the texts of an action: {fecha} / {date} (2026-10-16), {hora} / {time} (09:30:05),
/// {portapapeles} / {clipboard} (its text), {portapapeles:2} (5.0.368: the text copied before the last one, from the clipboard
/// history; :1 is the last one) and, inside a command (see <see cref="CommandVariables"/>), {resultado} /
/// {result}, {seleccion} / {selection} and the ones saved with a name. Anything else between braces is left alone (a
/// JSON body has many).
/// </summary>
public static partial class TextVariables
{
    [GeneratedRegex(@"\{([\p{L}_][\p{L}\p{N}_]*)(?::(\d{1,3}))?\}")]
    private static partial Regex Pattern();

    /// <summary>
    /// The N-th text of the clipboard history (1: the last one copied), or null when there is none or the history is off;
    /// the app sets it. Without it {portapapeles:1} is the clipboard and the others are empty.
    /// </summary>
    public static Func<int, string?>? HistoryText { get; set; }

    /// <summary>Whether <paramref name="text"/> uses {seleccion} (it has to be copied before the action runs).</summary>
    public static bool UsesSelection(string? text) =>
        !string.IsNullOrEmpty(text) && Pattern().Matches(text).Any(m => IsSelection(m.Groups[1].Value));

    /// <summary>Whether any text of <paramref name="action"/> uses {seleccion} (its JSON keeps accents as they are).</summary>
    public static bool UsesSelection(CommandAction action) => UsesSelection(Json.Serialize(action));

    private static bool IsSelection(string name) => name.ToLowerInvariant() is "seleccion" or "selección" or "selection";

    /// <param name="encode">Applied to every value put in (URL or JSON escaping).</param>
    /// <param name="variables">The command's; null: the run in progress (<see cref="CommandVariables.Current"/>).</param>
    public static string Expand(string text, Func<string, string>? encode = null, DateTime? now = null, Func<string>? clipboard = null,
        CommandVariables? variables = null)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        var time = now ?? DateTime.Now;
        var run = variables ?? CommandVariables.Current;
        return Pattern().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (m.Groups[2].Success)
            {
                // Only the clipboard takes a number; anything else with «:N» is left alone.
                if (name.ToLowerInvariant() is not ("portapapeles" or "clipboard")) return m.Value;
                var n = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var copied = n <= 0 ? null : HistoryText?.Invoke(n) ?? (n == 1 ? (clipboard ?? ClipboardText)() : string.Empty);
                return encode is null ? copied ?? string.Empty : encode(copied ?? string.Empty);
            }
            string? value = name.ToLowerInvariant() switch
            {
                "fecha" or "date" => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "hora" or "time" => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                "portapapeles" or "clipboard" => (clipboard ?? ClipboardText)(),
                "resultado" or "result" => run?.Result ?? string.Empty,
                _ when IsSelection(name) => run?.Selection ?? string.Empty,
                _ => run is not null && run.TryGet(name, out var saved) ? saved : null,
            };
            if (value is null) return m.Value;
            return encode is null ? value : encode(value);
        });
    }

    /// <summary>
    /// An address: the values go escaped (a search with {seleccion}), except a whole address that is only a variable
    /// ({portapapeles} with a link copied).
    /// </summary>
    public static string ExpandUrl(string url) =>
        Pattern().Match(url.Trim()) is { Success: true } m && m.Length == url.Trim().Length ? Expand(url).Trim() : Expand(url, Uri.EscapeDataString);

    /// <summary>Text on the clipboard (empty if there is none). Works from any thread.</summary>
    public static string ClipboardText()
    {
        // Another program may have it open for a moment.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!Win32.OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(20);
                continue;
            }
            try
            {
                var handle = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return string.Empty;
                var ptr = Win32.GlobalLock(handle);
                if (ptr == IntPtr.Zero) return string.Empty;
                try { return Marshal.PtrToStringUni(ptr) ?? string.Empty; }
                finally { Win32.GlobalUnlock(handle); }
            }
            finally { Win32.CloseClipboard(); }
        }
        return string.Empty;
    }
}
