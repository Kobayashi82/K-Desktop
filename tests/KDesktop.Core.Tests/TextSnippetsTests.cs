using System.Globalization;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>5.0.354: «Textos», the text expander (which abbreviation goes off and what it writes).</summary>
public class TextSnippetsTests
{
    private static TextSnippet Snippet(string abbreviation, string text = "x", bool enabled = true, params string[] apps) =>
        new() { Abbreviation = abbreviation, Text = text, Enabled = enabled, Processes = [.. apps] };

    private static string Notepad() => "notepad";

    /// <summary>5.0.358: a text limited to some programs.</summary>
    [Fact]
    public void ATextLimitedToProgramsGoesOffOnlyThereAndWinsOverOneForEverywhere()
    {
        var index = new SnippetIndex(
            [Snippet("sal", "Hola"), Snippet("sal", "Estimado:", true, "outlook"), Snippet("tg", "x", true, "telegram"), Snippet("tgx", "y", true, "chrome")],
            ";", ["telegram", "game"]);

        Assert.Equal("Hola", index.Typed(";sal", Notepad, out _)?.Text);
        Assert.Equal("Estimado:", index.Typed(";sal", () => "outlook", out _)?.Text);
        Assert.Null(index.Typed(";tg", Notepad, out _));
        // Its program is in «Desactivar en»: the text limited to it still works there; one for everywhere does not.
        Assert.NotNull(index.Typed(";tg", () => "telegram", out _));
        Assert.Null(index.Typed(";sal", () => "game", out _));
        // «;tgx» only works in chrome: in telegram, «;tg» does not wait for it.
        Assert.NotNull(index.Typed(";tg", () => "telegram", out _));
        // In K-Desktop's box to try them, every one works (so «;tg» waits there for «;tgx»).
        Assert.Null(index.Typed(";tg", () => SnippetIndex.AnyProgram, out _));
        Assert.Equal("x", index.Ended(";tg", () => SnippetIndex.AnyProgram, out _)?.Text);
        Assert.Equal("y", index.Typed(";tgx", () => SnippetIndex.AnyProgram, out _)?.Text);
        // The program is only asked for when an abbreviation is there.
        Assert.Null(index.Typed("hola mundo", () => throw new InvalidOperationException(), out _));
    }

    [Fact]
    public void AnAbbreviationGoesOffAsSoonAsItIsTypedUnlessALongerOneStartsTheSame()
    {
        var index = new SnippetIndex([Snippet("firma"), Snippet("fe"), Snippet("fecha"), Snippet("off", enabled: false)], ";", []);

        Assert.Equal("firma", index.Typed("hola ;firma", Notepad, out var typed)?.Abbreviation);
        Assert.Equal("firma", typed);
        Assert.Null(index.Typed("hola ;firm", Notepad, out _));
        Assert.Null(index.Typed("hola firma", Notepad, out _)); // without the prefix
        Assert.Null(index.Typed(";off", Notepad, out _)); // disabled
        // «;fe» waits: «;fecha» starts the same.
        Assert.Null(index.Typed(";fe", Notepad, out _));
        Assert.Equal("fe", index.Ended(";fe", Notepad, out _)?.Abbreviation);
        Assert.Equal("fecha", index.Typed(";fecha", Notepad, out _)?.Abbreviation);
        // The case does not matter to find it.
        Assert.Equal("firma", index.Typed(";FIRMA", Notepad, out typed)?.Abbreviation);
        Assert.Equal("FIRMA", typed);
    }

    /// <summary>
    /// 5.0.355: the hook reads each key's character with ToUnicodeEx; its buffer went as ANSI and Windows wrote past it
    /// (heap corruption: K-Desktop closed when typing with a text without prefix). Nothing is sent to the system here: the
    /// hook's handler is called directly and nobody listens to what it finds.
    /// </summary>
    [Fact]
    public void TheHookReadsTypedKeysWithoutBreakingMemory()
    {
        using var router = new HotkeyRouter();
        router.UpdateSnippets(new SnippetIndex([Snippet("hola")], "", []));
        for (var i = 0; i < 20_000; i++)
            foreach (var vk in "HOLA ")
            {
                router.OnKey(new KeyboardInput(vk, true, false, false));
                router.OnKey(new KeyboardInput(vk, false, false, false));
            }
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Fact]
    public void WithoutAPrefixOnlyWholeWordsGoOff()
    {
        var index = new SnippetIndex([Snippet("ok")], "", []);
        Assert.NotNull(index.Typed("ok", Notepad, out _));
        Assert.NotNull(index.Typed("vale, ok", Notepad, out _));
        Assert.Null(index.Typed("book", Notepad, out _));
    }

    [Fact]
    public void TheAbbreviationIsCleanedOfSpacesAndThePrefix()
    {
        Assert.Equal("firma", TextSnippet.Clean(" ;fir ma", ";"));
        Assert.Equal(";", TextSnippet.Clean(";", ";")); // only the prefix: kept, it is all there is
        var index = new SnippetIndex([Snippet(";dir")], ";", []);
        Assert.NotNull(index.Typed(";dir", Notepad, out _));
    }

    [Fact]
    public void TheTextKeepsTheCaseTypedAndTheCursor()
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var now = new DateTime(2026, 10, 29, 9, 30, 5);

        Assert.Equal(("Un saludo, Kobayashi", 0), TextExpander.Build("Un saludo, Kobayashi", "firma", "firma", false, culture: es));
        Assert.Equal(("UN SALUDO, KOBAYASHI", 0), TextExpander.Build("Un saludo, Kobayashi", "firma", "FIRMA", false, culture: es));
        Assert.Equal(("Hola qué tal", 0), TextExpander.Build("hola qué tal", "hq", "Hq", false, culture: es));
        // The cursor goes back to {cursor}; the space that ended it goes after the text.
        Assert.Equal(("<b></b> ", 5), TextExpander.Build("<b>{cursor}</b>", "b", "b", true, culture: es));
        Assert.Equal(("ÁBC", 1), TextExpander.Build("áb{cursor}c", "aa", "AA", false, culture: es));
        // Variables, with the clipboard asked only when used.
        Assert.Equal(("2026-10-29 copiado", 0), TextExpander.Build("{fecha} {portapapeles}", "f", "f", false, () => "copiado", now, es));
        Assert.Equal(("a\nb", 0), TextExpander.Build("a\r\nb", "x", "x", false, culture: es));
    }
}
