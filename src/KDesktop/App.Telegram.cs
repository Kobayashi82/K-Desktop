using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop;

/// <summary>
/// Telegram in K-Desktop's process (telegram, phase 1): the bot listens and the owner is linked with a code (Opciones ›
/// Cuentas › Telegram Bot); K-Desktop tells the owner the important mails. Works with the assistant off. Phase 6: the
/// account with a number (› Telegram) is another connection just like it, with its own owner, contacts and options;
/// each one answers by where it was written to.
/// </summary>
public partial class App
{
    /// <summary>The bot.</summary>
    public TelegramService Telegram { get; private set; } = null!;

    /// <summary>The account with a number (phase 6).</summary>
    public TelegramService TelegramAccount { get; private set; } = null!;

    public TelegramService TelegramOf(TelegramVia via) => via == TelegramVia.Account ? TelegramAccount : Telegram;

    public TelegramCommands TelegramCommandsOf(TelegramVia via) => via == TelegramVia.Account ? TelegramAccountCommands : TelegramCommands;

    private void CreateTelegram()
    {
        Telegram = new TelegramService(TelegramService.DefaultFolder, Secrets.Protect, Secrets.Unprotect);
        TelegramAccount = new TelegramService(TelegramService.AccountFolder, Secrets.Protect, Secrets.Unprotect, kind: TelegramVia.Account);
        TelegramCommands = WireTelegram(Telegram, "settings/accounts/telegrambot");
        TelegramAccountCommands = WireTelegram(TelegramAccount, "settings/accounts/telegram");
        Engine.Telegram = Telegram;
        Engine.TelegramAccount = TelegramAccount;
        TelegramSendAction.NameOf = id => long.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var chat)
            ? TelegramAccount.Read(s => s.Contacts.Any(c => c.Id == chat)) ? TelegramAccount.NameOf(chat) : Telegram.NameOf(chat)
            : null;
        Events.Telegram = TelegramCommands;
        Events.TelegramAccount = TelegramAccountCommands;
        WireTelegramTalk();
    }

    private TelegramCommands WireTelegram(TelegramService telegram, string page)
    {
        telegram.OwnerLinked += owner => Dispatcher.BeginInvoke(() =>
            Ui.ShowOsd(Loc.T("telegram.linked", owner.Shown), OsdKind.Success, null, OsdLook.Of("Send24")));
        // Someone new wrote: told here too (the owner decides on the PC or with the buttons in Telegram).
        telegram.StrangerArrived += person => Dispatcher.BeginInvoke(() =>
            _ = Ui.ShowNotificationAsync(new Notification(Loc.T("telegram.stranger.title"), Loc.T("telegram.stranger.text", person.Shown), "kd:Send24:blue", NotifyColor.Blue,
                ShowNotificationAction.GlobalSound, TimeSpan.FromSeconds(15), null, () => ShowMainWindow(w => w.Navigate(page)))));
        // «Solo yo mientras juego»: something covers the monitor of the window in front.
        telegram.Busy = () => Native.UiNative.FullscreenOn(Native.UiNative.MonitorFor(Core.Model.ScreenChoice.ActiveWindow).Monitor);
        return new TelegramCommands(telegram, () => Dispatcher.Invoke(() => Config.Commands.ToList()), cmd => Engine.RunAsync(cmd, TriggerSource.Telegram),
            cmd => Core.Assistant.ActionRisk.OfCommand(cmd, id => Config.FindCommand(id)))
        {
            // At the PC: the mouse or the keyboard were used in the last minute and a half.
            OwnerPresent = () => ProcessSnapshot.IdleTime() < TimeSpan.FromSeconds(90),
            AskOnPc = question => Dispatcher.InvokeAsync(() => AskOnPcAsync(question)).Task.Unwrap(),
            Policy = () => Config.Settings.AssistantAskBefore,
            // A voice note (phase 5.1): Parakeet, as «Grabar audio» › «Al terminar, transcribir».
            Transcribe = async (path, token) => Core.Voice.VoiceModels.Ready
                ? await Core.Voice.AudioTranscriber.TranscribeAsync(Voice.SpeechToText, path, token).ConfigureAwait(false)
                : null,
        };
    }

    /// <summary>What is asked to the bot without the assistant: /comandos, running them, confirming, the files that arrive.</summary>
    public TelegramCommands TelegramCommands { get; private set; } = null!;

    /// <summary>The same for the account with a number.</summary>
    public TelegramCommands TelegramAccountCommands { get; private set; } = null!;

    /// <summary>
    /// A confirmation of something asked by Telegram, on the PC (the window of the assistants' questions, as K-Desktop):
    /// true, false, or null when nobody answered (then it is asked by Telegram).
    /// </summary>
    private static async Task<bool?> AskOnPcAsync(string question)
    {
        var window = new Views.Assistant.AskWindow("K-Desktop", Views.AssistantLook.IconOf(Core.Assistant.AssistantTargeting.KDesktop), question, false, TimeSpan.FromSeconds(30));
        window.Show();
        var choice = await window.Answer;
        Log.Info($"Telegram: «{question}» en el PC → {choice}");
        return choice switch
        {
            "yes" => true,
            "no" => false,
            _ => null,
        };
    }

    private void StartTelegram()
    {
        try
        {
            Telegram.Start();
            TelegramAccount.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Telegram: no se ha podido iniciar", ex);
        }
    }

    /// <summary>An important mail, to the owner by Telegram (› Avisos of the bot and of the account, each one if marked).</summary>
    private void TellMailByTelegram(Core.Mail.MailArrived arrived)
    {
        if (!arrived.Important || arrived.Account.Notify == Core.Mail.MailNotify.None) return;
        var m = arrived.Mail;
        var subject = string.IsNullOrWhiteSpace(m.Subject) ? Loc.T("mail.noSubject") : m.Subject;
        foreach (var telegram in (TelegramService[])[Telegram, TelegramAccount])
            if (telegram.Read(s => s.Notices.ImportantMail))
                telegram.SendToOwner($"✉️ {Loc.T("telegram.notice.mail", m.From.Shown)}\n{subject}" + (m.Snippet.Length > 0 ? "\n\n" + m.Snippet : string.Empty));
    }
}
