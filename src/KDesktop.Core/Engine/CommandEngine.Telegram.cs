using System.Globalization;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    /// <summary>Telegram («Enviar por Telegram»); set by K-Desktop at start.</summary>
    public TelegramService? Telegram { get; set; }

    /// <summary>Telegram takes a photo up to 10 MB; a bigger capture goes as a file.</summary>
    private const long MaxPhoto = 10L << 20;

    private async Task TelegramSendAsync(TelegramSendAction a, ExecutionContext ctx)
    {
        if (Telegram is not { Bot.Enabled: true } telegram) throw new InvalidOperationException(Loc.T("telegram.error.notReady"));
        var to = TextVariables.Expand(a.To);
        var chat = telegram.Recipient(to) ?? throw new InvalidOperationException(Loc.T(to.Trim().Length == 0 ? "telegram.error.noOwner" : "telegram.error.noRecipient", to));
        if (!telegram.CanWriteTo(chat)) throw new InvalidOperationException(Loc.T("telegram.error.cantWrite", telegram.NameOf(chat)));
        var text = TextVariables.Expand(a.Text);
        string? file = null;
        var kind = TelegramFileKind.Document;
        var temporary = false;
        var folder = Path.Combine(Path.GetTempPath(), "K-Desktop Telegram");
        switch (a.What)
        {
            case TelegramSendWhat.File:
                file = TextVariables.Expand(a.File).Trim().Trim('"');
                if (file.Length == 0 || !File.Exists(file)) throw new InvalidOperationException(Loc.T("telegram.error.noFile", file));
                break;
            case TelegramSendWhat.Screenshot:
                Directory.CreateDirectory(folder);
                file = Path.Combine(folder, $"{Loc.T("telegram.file.capture")} {DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}.png");
                await File.WriteAllBytesAsync(file, Png.Encode(ScreenCapture.Capture(ScreenCapture.VirtualScreen)), ctx.Token).ConfigureAwait(false);
                kind = new FileInfo(file).Length <= MaxPhoto ? TelegramFileKind.Photo : TelegramFileKind.Document;
                temporary = true;
                break;
            case TelegramSendWhat.Clipboard:
                if (ClipboardSnapshot.Files() is { Count: > 0 } files) file = files[0];
                else if (ClipboardSnapshot.SaveImage(folder) is { } image)
                {
                    file = image;
                    kind = TelegramFileKind.Photo;
                    temporary = true;
                }
                else
                {
                    var clip = TextVariables.ClipboardText();
                    if (clip.Trim().Length == 0) throw new InvalidOperationException(Loc.T("telegram.error.emptyClipboard"));
                    text = text.Trim().Length == 0 ? clip : text + "\n\n" + clip;
                }
                break;
        }
        if (file is null && text.Trim().Length == 0) throw new InvalidOperationException(Loc.T("telegram.error.nothing"));
        ctx.Token.ThrowIfCancellationRequested();
        try
        {
            if (!await telegram.SendAsync(chat, text, file, kind).WaitAsync(ctx.Token).ConfigureAwait(false))
                throw new InvalidOperationException(Loc.T("telegram.error.notSent", telegram.Problem ?? Loc.T("telegram.error.network")));
        }
        finally
        {
            if (temporary && file is not null)
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        var who = telegram.NameOf(chat);
        if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.telegramSent", chat == telegram.Owner?.Id ? Loc.T("summary.telegram.owner") : who), OsdKind.Success, null, OsdLook.Of("Send24"));
        Gave(a, who);
    }
}
