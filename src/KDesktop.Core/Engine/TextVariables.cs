using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KDesktop.Core.Native;

namespace KDesktop.Core.Engine;

/// <summary>
/// Variables inside the texts of an action: {fecha} / {date} (2026-10-16), {hora} / {time} (09:30:05) and
/// {portapapeles} / {clipboard} (its text). Anything else between braces is left alone (a JSON body has many).
/// </summary>
public static partial class TextVariables
{
    [GeneratedRegex(@"\{(fecha|date|hora|time|portapapeles|clipboard)\}", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <param name="encode">Applied to every value put in (URL or JSON escaping).</param>
    public static string Expand(string text, Func<string, string>? encode = null, DateTime? now = null, Func<string>? clipboard = null)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        var time = now ?? DateTime.Now;
        return Pattern().Replace(text, m =>
        {
            var value = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "fecha" or "date" => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "hora" or "time" => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                _ => (clipboard ?? ClipboardText)(),
            };
            return encode is null ? value : encode(value);
        });
    }

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
