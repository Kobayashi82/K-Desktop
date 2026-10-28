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

    /// <summary>read_clipboard (5.0.348): without text, the files or the picture copied; never Ctrl+C.</summary>
    [Fact]
    public async Task EverythingCopiedForReadClipboard()
    {
        var files = new Desk { CopiedFiles = [@"C:\Fotos\a.jpg", @"C:\Fotos\b.jpg"], Picture = @"C:\tmp\x.png" };
        var found = await Grab(files, AssistantText.ClipboardAll);
        Assert.Equal(("files", 2), (found.Reason, found.Files!.Count));
        var picture = await Grab(new Desk { Picture = @"C:\tmp\x.png" }, AssistantText.ClipboardAll);
        Assert.Equal(("image", @"C:\tmp\x.png"), (picture.Reason, picture.Image));
        // Text wins; and the other sources still only want text.
        Assert.Equal("hola", (await Grab(new Desk { Clipboard = "hola", Picture = "p" }, AssistantText.ClipboardAll)).Text);
        Assert.Equal("empty", (await Grab(new Desk { Picture = "p" }, "clipboard")).Reason);
        Assert.Empty(files.Keys);
    }

    /// <summary>A copied picture without PNG: its DIB as a .bmp that Windows decodes, pixels in place.</summary>
    [Fact]
    public async Task ADibBecomesABmpThatDecodes()
    {
        // 2×1, 32 bits, BI_BITFIELDS after a plain header (as screenshots come): red then blue, bottom-up.
        var dib = new byte[40 + 12 + 8];
        BitConverter.TryWriteBytes(dib.AsSpan(0), 40);
        BitConverter.TryWriteBytes(dib.AsSpan(4), 2);
        BitConverter.TryWriteBytes(dib.AsSpan(8), 1);
        BitConverter.TryWriteBytes(dib.AsSpan(12), (ushort)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14), (ushort)32);
        BitConverter.TryWriteBytes(dib.AsSpan(16), 3);
        BitConverter.TryWriteBytes(dib.AsSpan(40), 0x00FF0000);
        BitConverter.TryWriteBytes(dib.AsSpan(44), 0x0000FF00);
        BitConverter.TryWriteBytes(dib.AsSpan(48), 0x000000FF);
        byte[] pixels = [0, 0, 255, 255, 255, 0, 0, 255]; // BGRA: red, blue
        pixels.CopyTo(dib, 52);
        var bmp = Platform.ClipboardSnapshot.BmpFromDib(dib);

        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bmp);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        Assert.Equal((2u, 1u), (decoder.PixelWidth, decoder.PixelHeight));
        var data = (await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
        Assert.Equal((byte)255, data[2]); // red
        Assert.Equal((byte)255, data[4]); // blue
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

        public IReadOnlyList<string> CopiedFiles { get; init; } = [];

        public string? Picture { get; init; }

        public IReadOnlyList<string> Files() => CopiedFiles;

        public string? SaveImage() => Picture;
    }
}
