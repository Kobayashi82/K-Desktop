using System.Globalization;
using KDesktop.Core.Input;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

/// <summary>
/// Writes the text of an abbreviation typed («Textos»): erases what was typed, writes the text with its variables and
/// leaves the cursor at {cursor}. Short texts are typed; long ones, or with several lines (Enter would send a chat
/// message), are pasted borrowing the clipboard, which is put back as it was. Never in a password box.
/// </summary>
public sealed class TextExpander : IDisposable
{
    /// <summary>Beyond this the text is pasted instead of typed.</summary>
    public const int TypeLimit = 200;

    public const string CursorMark = "{cursor}";

    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>Whether the box with the focus is for a password (the app asks UI Automation). Null: never.</summary>
    public Func<bool>? IsPasswordBox { get; set; }

    /// <summary>Raised after a text was written (statistics).</summary>
    public event Action<SnippetHit>? Expanded;

    public async Task ExpandAsync(SnippetHit hit)
    {
        // One after another: two abbreviations typed fast never mix their keys.
        await _one.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsPasswordBox?.Invoke() == true)
            {
                GiveBack(hit);
                Log.Info("Textos: no se escribe en una contraseña");
                return;
            }
            var (text, back) = Build(hit.Snippet.Text, hit.Snippet.Abbreviation, hit.Typed, hit.EndVk == KeyNames.VK_SPACE);
            InputSimulator.ReleaseModifiers();
            for (var i = 0; i < hit.Erase; i++) InputSimulator.KeyPress(KeyNames.VK_BACK);
            if (text.Contains('\n') || text.Length > TypeLimit) await PasteAsync(text, back).ConfigureAwait(false);
            else
            {
                InputSimulator.SendText(text, releaseModifiers: false);
                Back(back);
            }
            Log.Info($"Textos: «{hit.Typed}» escrito");
            Expanded?.Invoke(hit);
        }
        catch (Exception ex)
        {
            Log.Error("Textos: no se pudo escribir", ex);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Not written: the key that was swallowed goes to the program after all.</summary>
    private static void GiveBack(SnippetHit hit)
    {
        if (hit.EndVk != 0) InputSimulator.KeyPress(hit.EndVk);
        else InputSimulator.SendText(hit.Last.ToString(), releaseModifiers: false);
    }

    private static async Task PasteAsync(string text, int back)
    {
        var saved = ClipboardSnapshot.Take();
        if (!ClipboardSnapshot.SetText(text, passing: true))
        {
            Log.Warn("Textos: el portapapeles está ocupado; se escribe tecla a tecla");
            InputSimulator.SendText(text, releaseModifiers: false);
            Back(back);
            return;
        }
        await Task.Delay(40).ConfigureAwait(false);
        InputSimulator.SendCombo("CTRL + V", releaseModifiers: false);
        await Task.Delay(120).ConfigureAwait(false);
        Back(back);
        await Task.Delay(Assistant.AssistantText.PasteSettle).ConfigureAwait(false);
        saved?.Restore();
    }

    private static void Back(int count)
    {
        for (var i = 0; i < count; i++) InputSimulator.KeyPress(0x25 /* VK_LEFT */, extended: true);
    }

    /// <summary>
    /// The text to write and how many places the cursor goes back to stay at {cursor}. The case of what was typed carries
    /// over: «;FIRMA» writes it in capitals and «;Firma» with the first letter in capitals (when the abbreviation is not
    /// written that way already). <paramref name="space"/>: it ended with a space, which goes after the text.
    /// </summary>
    public static (string Text, int Back) Build(string template, string abbreviation, string typed, bool space,
        Func<string>? clipboard = null, DateTime? now = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var text = TextVariables.Expand(template, now: now, clipboard: clipboard).ReplaceLineEndings("\n");
        if (HasLetters(typed) && !typed.Equals(abbreviation, StringComparison.Ordinal))
        {
            if (typed.Length > 1 && typed.All(c => !char.IsLower(c))) text = Upper(text, culture);
            else if (char.IsUpper(typed.First(char.IsLetter)) && !char.IsUpper(abbreviation.FirstOrDefault(char.IsLetter))) text = Capitalized(text, culture);
        }
        if (space) text += " ";
        var at = text.IndexOf(CursorMark, StringComparison.OrdinalIgnoreCase);
        text = text.Replace(CursorMark, string.Empty, StringComparison.OrdinalIgnoreCase);
        return (text, at < 0 ? 0 : text.Length - at);
    }

    public void Dispose() => _one.Dispose();

    private static bool HasLetters(string s) => s.Any(char.IsLetter);

    /// <summary>In capitals, but {cursor} stays as it is.</summary>
    private static string Upper(string text, CultureInfo culture) =>
        string.Join(CursorMark, text.Split(CursorMark, StringSplitOptions.None).Select(p => p.ToUpper(culture)));

    private static string Capitalized(string text, CultureInfo culture)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' && text.AsSpan(i).StartsWith(CursorMark, StringComparison.OrdinalIgnoreCase)) { i += CursorMark.Length - 1; continue; }
            if (!char.IsLetter(text[i])) continue;
            return text[..i] + char.ToUpper(text[i], culture) + text[(i + 1)..];
        }
        return text;
    }
}
