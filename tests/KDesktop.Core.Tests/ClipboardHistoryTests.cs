using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>5.0.363: the clipboard history (what it keeps, for how long, and that it opens again encrypted).</summary>
public sealed class ClipboardHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-clip-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ClipCapture Text(string text) => new(ClipKind.Text, text);

    [Fact]
    public void TheSameCopiedAgainGoesBackToTheTopInsteadOfRepeating()
    {
        var history = new ClipboardHistory(_folder);
        history.Add(Text("uno"));
        history.Add(Text("dos"));
        history.Add(new ClipCapture(ClipKind.Text, "uno", Rtf: "{\\rtf1 uno}"));

        Assert.Equal(["uno", "dos"], history.Entries.Select(e => e.Text));
        Assert.True(history.Entries[0].HasFormat); // the formats of the latest copy
        Assert.Null(history.Add(Text("   "))); // nothing to keep
    }

    [Fact]
    public void BeyondTheLimitTheOldestGoButPinnedOnesStay()
    {
        var history = new ClipboardHistory(_folder) { MaxItems = 3 };
        var first = history.Add(Text("a"))!;
        history.SetPinned(first.Id, true);
        foreach (var t in new[] { "b", "c", "d", "e" }) history.Add(Text(t));

        Assert.Equal(["e", "d", "c", "a"], history.Entries.Select(e => e.Text));

        history.Clear();
        Assert.Equal(["a"], history.Entries.Select(e => e.Text));
        history.Clear(pinnedToo: true);
        Assert.Empty(history.Entries);
    }

    /// <summary>5.0.368: {portapapeles:N} is the N-th text copied (pictures and files aside).</summary>
    [Fact]
    public void TheClipboardVariableTakesWhatWasCopiedBefore()
    {
        var history = new ClipboardHistory(_folder);
        history.Add(Text("primero"));
        history.Add(new ClipCapture(ClipKind.Files, string.Empty, Files: [@"C:\a.txt"]));
        history.Add(Text("segundo"));
        Assert.Equal("segundo", history.Text(1));
        Assert.Equal("primero", history.Text(2));
        Assert.Null(history.Text(3));

        Assert.Equal("ahora · primero · ", Expand(history.Text, "{portapapeles} · {portapapeles:2} · {portapapeles:9}"));
    }

    /// <summary>Expands with the history given (the app sets it for real); {x:2} of anything else is left as it is.</summary>
    private static string Expand(Func<int, string?> history, string text)
    {
        var before = Engine.TextVariables.HistoryText;
        Engine.TextVariables.HistoryText = history;
        try { return Engine.TextVariables.Expand(text, clipboard: () => "ahora"); }
        finally { Engine.TextVariables.HistoryText = before; }
    }

    [Fact]
    public void WithoutAHistoryOnlyTheLastOneIsKnown()
    {
        Assert.Equal("ahora|ahora||{fecha:2}", Engine.TextVariables.Expand("{portapapeles}|{portapapeles:1}|{portapapeles:2}|{fecha:2}", clipboard: () => "ahora"));
    }

    [Fact]
    public void OldEntriesGoAfterTheDaysChosen()
    {
        var history = new ClipboardHistory(_folder);
        history.Add(Text("viejo"))!.Copied = DateTime.Now.AddDays(-10);
        history.Add(Text("nuevo"));
        history.MaxDays = 7;
        history.Trim();
        Assert.Equal(["nuevo"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void ItIsSavedEncryptedAndOpensAgainWithItsPictures()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        var history = new ClipboardHistory(_folder);
        history.Add(Text("contraseña de prueba"));
        var image = history.Add(new ClipCapture(ClipKind.Image, string.Empty, Png: png, Width: 2, Height: 1))!;
        history.Add(new ClipCapture(ClipKind.Files, string.Empty, Files: [@"C:\a.txt", @"C:\b.txt"]));

        // Nothing readable on disk.
        foreach (var file in Directory.GetFiles(_folder))
            Assert.DoesNotContain("contraseña", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);

        var again = new ClipboardHistory(_folder);
        again.Load();
        Assert.Equal([ClipKind.Files, ClipKind.Image, ClipKind.Text], again.Entries.Select(e => e.Kind));
        Assert.Equal(png, again.ImageBytes(image.Id));
        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], again.Entries[0].Files);

        again.Remove(image.Id);
        Assert.Null(again.ImageBytes(image.Id));
    }
}
