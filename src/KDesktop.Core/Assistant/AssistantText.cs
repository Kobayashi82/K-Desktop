using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Native;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>What <see cref="AssistantText"/> touches: the window in front, the clipboard and two key presses (tests pretend).</summary>
public interface ITextDesk
{
    ForegroundWindow Foreground();

    /// <summary>What the clipboard holds now, to put back (null: it could not be kept).</summary>
    object? Save();

    void Restore(object? saved);

    /// <summary>Ctrl+C to the window in front; whether the clipboard changed (something was selected).</summary>
    Task<bool> CopyAsync(CancellationToken token);

    /// <summary>The text in the clipboard («» without text).</summary>
    string Read();

    /// <summary>Leaves a text in the clipboard; <paramref name="passing"/>: only to paste it (kept out of Windows' history).</summary>
    bool Write(string text, bool passing);

    /// <summary>Ctrl+V to the window in front.</summary>
    void Paste();

    /// <summary>The files copied in the Explorer (none: empty).</summary>
    IReadOnlyList<string> Files() => [];

    /// <summary>The picture copied, saved to a temporary file; null: none.</summary>
    string? SaveImage() => null;
}

/// <summary>The real desk: the foreground window, the Windows clipboard and simulated Ctrl+C / Ctrl+V.</summary>
public sealed class SystemTextDesk : ITextDesk
{
    public ForegroundWindow Foreground() => ForegroundWindow.Capture();

    public object? Save() => ClipboardSnapshot.Take();

    public void Restore(object? saved)
    {
        if (saved is ClipboardSnapshot snapshot && !snapshot.Restore()) Log.Warn("Portapapeles: no se ha podido devolver a como estaba");
    }

    public async Task<bool> CopyAsync(CancellationToken token)
    {
        var before = Win32.GetClipboardSequenceNumber();
        InputSimulator.SendCombo("CTRL + C");
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(30, token).ConfigureAwait(false);
            if (Win32.GetClipboardSequenceNumber() != before)
            {
                await Task.Delay(30, token).ConfigureAwait(false); // let it finish writing every format
                return true;
            }
        }
        return false;
    }

    public string Read() => Engine.TextVariables.ClipboardText();

    public IReadOnlyList<string> Files() => ClipboardSnapshot.Files();

    /// <summary>Where the pictures for the assistants go; any older than a few minutes is removed on the way.</summary>
    public static string ImageFolder => Path.Combine(Path.GetTempPath(), "K-Desktop", "Clipboard");

    public string? SaveImage()
    {
        try
        {
            if (Directory.Exists(ImageFolder))
                foreach (var old in new DirectoryInfo(ImageFolder).EnumerateFiles().Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddMinutes(-10)))
                    old.Delete();
            return ClipboardSnapshot.SaveImage(ImageFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Portapapeles: no se ha podido guardar la imagen: {ex.Message}");
            return null;
        }
    }

    public bool Write(string text, bool passing) => ClipboardSnapshot.SetText(text, passing);

    public void Paste() => InputSimulator.SendCombo("CTRL + V");
}

/// <summary>
/// K-Desktop's side of a <see cref="TextMessage"/> (stage 8, <c>transformar_texto</c>): the assistant's process has no
/// rights over elevated windows, K-Desktop does. Taking: the selection of the window in front (Ctrl+C, and the clipboard
/// put back as it was) or, without one, what is copied. Putting: pasted in that same window (the clipboard put back after)
/// or left copied; never pasted in another window (the user moved on: it is left copied).
/// </summary>
public static class AssistantText
{
    /// <summary>How long a program may take to read the clipboard after Ctrl+V, before it is put back.</summary>
    public static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(600);

    /// <summary>The source of read_clipboard: what is copied, also files or a picture.</summary>
    public const string ClipboardAll = "clipboardAll";

    /// <summary>More than this is not worked on (a whole book selected).</summary>
    public const int MaxLength = 20_000;

    public static async Task<TextDoneMessage> HandleAsync(TextMessage message, ITextDesk desk, CancellationToken token = default)
    {
        try
        {
            return message.Op == "put"
                ? await PutAsync(message, desk, token).ConfigureAwait(false)
                : await GrabAsync(message, desk, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Asistente: no se ha podido {(message.Op == "put" ? "poner" : "coger")} el texto: {ex.Message}");
            return new TextDoneMessage(message.Run, false, Reason: "busy");
        }
    }

    private static async Task<TextDoneMessage> GrabAsync(TextMessage message, ITextDesk desk, CancellationToken token)
    {
        var front = desk.Foreground();
        // K-Desktop's own windows (the chat, the box to write) have nothing selected for this: what is copied.
        var usable = front.Handle != IntPtr.Zero && !IsOwn(front.ProcessName);
        if (message.Source is not ("clipboard" or ClipboardAll) && usable)
        {
            var saved = desk.Save();
            if (await desk.CopyAsync(token).ConfigureAwait(false))
            {
                var selected = desk.Read();
                desk.Restore(saved);
                if (selected.Trim().Length > 0) return Taken(message.Run, selected, "selection", front);
            }
        }
        if (message.Source == "selection") return new TextDoneMessage(message.Run, false, Reason: "noSelection");
        var copied = desk.Read();
        if (copied.Trim().Length > 0) return Taken(message.Run, copied, "clipboard", usable ? front : ForegroundWindow.None);
        // read_clipboard (5.0.348): without text, the files or the picture copied.
        if (message.Source == ClipboardAll)
        {
            if (desk.Files() is { Count: > 0 } files) return new TextDoneMessage(message.Run, false, Reason: "files", Files: files);
            if (desk.SaveImage() is { } image) return new TextDoneMessage(message.Run, false, Reason: "image", Image: image);
        }
        return new TextDoneMessage(message.Run, false, Reason: "empty");
    }

    private static TextDoneMessage Taken(string run, string text, string from, ForegroundWindow front) =>
        text.Length > MaxLength
            ? new TextDoneMessage(run, false, Reason: "tooLong")
            : new TextDoneMessage(run, true, text, from, (long)front.Handle, front.Title);

    private static async Task<TextDoneMessage> PutAsync(TextMessage message, ITextDesk desk, CancellationToken token)
    {
        if (message.Text.Length == 0) return new TextDoneMessage(message.Run, false, Reason: "empty");
        if (message.Mode == "paste")
        {
            var front = desk.Foreground();
            var same = message.Hwnd != 0 && ((long)front.Handle == message.Hwnd || (long)front.RootHandle == message.Hwnd);
            if (same)
            {
                var saved = desk.Save();
                if (!desk.Write(message.Text, passing: true)) return new TextDoneMessage(message.Run, false, Reason: "busy");
                await Task.Delay(40, token).ConfigureAwait(false);
                desk.Paste();
                await Task.Delay(PasteSettle, token).ConfigureAwait(false);
                if (saved is not null) desk.Restore(saved);
                return new TextDoneMessage(message.Run, true, From: "pasted", Hwnd: message.Hwnd);
            }
            // Another window in front now (or none given): never pasted somewhere else.
            return desk.Write(message.Text, passing: false)
                ? new TextDoneMessage(message.Run, true, From: "copied", Reason: message.Hwnd != 0 ? "moved" : null)
                : new TextDoneMessage(message.Run, false, Reason: "busy");
        }
        return desk.Write(message.Text, passing: false)
            ? new TextDoneMessage(message.Run, true, From: "copied")
            : new TextDoneMessage(message.Run, false, Reason: "busy");
    }

    /// <summary>K-Desktop itself (and the assistants' process, the same exe).</summary>
    private static bool IsOwn(string process) =>
        process.Equals(Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath), StringComparison.OrdinalIgnoreCase);
}
