using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;

namespace KDesktop.Core.Tests;

/// <summary>Stage 8 (<c>transformar_texto</c>): K-Desktop takes the text and puts the result back, leaving the clipboard as it was.</summary>
public class AssistantTextTests
{
    private static readonly ForegroundWindow Word = new(100, 7, "winword");

    private static Task<TextDoneMessage> Grab(Desk desk, string source = "auto") =>
        AssistantText.HandleAsync(new TextMessage("lander", "c-1", "grab", source), desk);

    private static Task<TextDoneMessage> Put(Desk desk, string text, string mode = "paste", long hwnd = 100) =>
        AssistantText.HandleAsync(new TextMessage("lander", "c-2", "put", Text: text, Mode: mode, Hwnd: hwnd), desk);

    [Fact]
    public async Task TheSelectionIsTakenAndTheClipboardLeftAsItWas()
    {
        var desk = new Desk { Clipboard = "lo de antes", Selected = "hola qe tal" };
        var taken = await Grab(desk);
        Assert.True(taken.Ok);
        Assert.Equal(("hola qe tal", "selection", 100L), (taken.Text, taken.From, taken.Hwnd));
        Assert.Equal("lo de antes", desk.Clipboard);
        Assert.Equal(["copy"], desk.Keys);
    }

    [Fact]
    public async Task WithoutASelectionWhatIsCopied()
    {
        var desk = new Desk { Clipboard = "texto copiado" };
        var taken = await Grab(desk);
        Assert.Equal(("texto copiado", "clipboard"), (taken.Text, taken.From));
        Assert.False((await Grab(desk, "selection")).Ok);
        Assert.Equal("noSelection", (await Grab(desk, "selection")).Reason);
        Assert.Equal("empty", (await Grab(new Desk())).Reason);
    }

    [Fact]
    public async Task FromKDesktopsOwnWindowsOnlyWhatIsCopied()
    {
        var own = Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath);
        var desk = new Desk { Front = new ForegroundWindow(5, 1, own), Clipboard = "copiado", Selected = "nada" };
        var taken = await Grab(desk);
        Assert.Equal(("copiado", "clipboard", 0L), (taken.Text, taken.From, taken.Hwnd));
        Assert.Empty(desk.Keys);
    }

    [Fact]
    public async Task TheResultIsPastedThereAndTheClipboardPutBack()
    {
        var desk = new Desk { Clipboard = "lo de antes" };
        var put = await Put(desk, "Hola, ¿qué tal?");
        Assert.Equal((true, "pasted"), (put.Ok, put.From));
        Assert.Equal(["paste:Hola, ¿qué tal?"], desk.Keys);
        Assert.True(desk.Passing);
        Assert.Equal("lo de antes", desk.Clipboard);
    }

    [Fact]
    public async Task NeverPastedInAnotherWindow()
    {
        var desk = new Desk { Front = new ForegroundWindow(200, 9, "chrome"), Clipboard = "lo de antes" };
        var put = await Put(desk, "resultado");
        Assert.Equal((true, "copied", "moved"), (put.Ok, put.From, put.Reason));
        Assert.Empty(desk.Keys);
        Assert.Equal("resultado", desk.Clipboard);
        Assert.False(desk.Passing);
        // Asked to copy: copied, wherever it came from.
        var copy = await Put(new Desk(), "otro", "copy");
        Assert.Equal(("copied", null), (copy.From, copy.Reason));
    }

    private sealed class Desk : ITextDesk
    {
        public ForegroundWindow Front { get; init; } = Word;
        public string Clipboard { get; set; } = string.Empty;
        public string? Selected { get; init; }
        public bool Passing { get; private set; }
        public List<string> Keys { get; } = [];

        public ForegroundWindow Foreground() => Front;

        public object? Save() => Clipboard;

        public void Restore(object? saved) => Clipboard = (string)saved!;

        public Task<bool> CopyAsync(CancellationToken token)
        {
            Keys.Add("copy");
            if (Selected is null) return Task.FromResult(false);
            Clipboard = Selected;
            return Task.FromResult(true);
        }

        public string Read() => Clipboard;

        public bool Write(string text, bool passing)
        {
            Clipboard = text;
            Passing = passing;
            return true;
        }

        public void Paste() => Keys.Add("paste:" + Clipboard);
    }
}
