using System.Globalization;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>5.0.354: «Textos», the text expander (which abbreviation goes off and what it writes).</summary>
public class TextSnippetsTests
{
    private static TextSnippet Snippet(string abbreviation, string text = "x", bool enabled = true) =>
        new() { Abbreviation = abbreviation, Text = text, Enabled = enabled };

    [Fact]
    public void AnAbbreviationGoesOffAsSoonAsItIsTypedUnlessALongerOneStartsTheSame()
    {
        var index = new SnippetIndex([Snippet("firma"), Snippet("fe"), Snippet("fecha"), Snippet("off", enabled: false)], ";", []);

        Assert.Equal("firma", index.Typed("hola ;firma", out var typed)?.Abbreviation);
        Assert.Equal("firma", typed);
        Assert.Null(index.Typed("hola ;firm", out _));
        Assert.Null(index.Typed("hola firma", out _)); // without the prefix
        Assert.Null(index.Typed(";off", out _)); // disabled
        // «;fe» waits: «;fecha» starts the same.
        Assert.Null(index.Typed(";fe", out _));
        Assert.Equal("fe", index.Ended(";fe", out _)?.Abbreviation);
        Assert.Equal("fecha", index.Typed(";fecha", out _)?.Abbreviation);
        // The case does not matter to find it.
        Assert.Equal("firma", index.Typed(";FIRMA", out typed)?.Abbreviation);
        Assert.Equal("FIRMA", typed);
    }

    [Fact]
    public void WithoutAPrefixOnlyWholeWordsGoOff()
    {
        var index = new SnippetIndex([Snippet("ok")], "", []);
        Assert.NotNull(index.Typed("ok", out _));
        Assert.NotNull(index.Typed("vale, ok", out _));
        Assert.Null(index.Typed("book", out _));
    }

    [Fact]
    public void TheAbbreviationIsCleanedOfSpacesAndThePrefix()
    {
        Assert.Equal("firma", TextSnippet.Clean(" ;fir ma", ";"));
        Assert.Equal(";", TextSnippet.Clean(";", ";")); // only the prefix: kept, it is all there is
        var index = new SnippetIndex([Snippet(";dir")], ";", []);
        Assert.NotNull(index.Typed(";dir", out _));
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
