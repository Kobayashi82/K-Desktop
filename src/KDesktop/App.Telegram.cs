using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop;

/// <summary>
/// Telegram in K-Desktop's process (telegram, phase 1): the bot listens and the owner is linked with a code (Opciones ›
/// Cuentas › Telegram); K-Desktop tells the owner the important mails. Works with the assistant off.
/// </summary>
public partial class App
{
    public TelegramService Telegram { get; private set; } = null!;

    private void CreateTelegram()
    {
        Telegram = new TelegramService(TelegramService.DefaultFolder, Secrets.Protect, Secrets.Unprotect);
        Telegram.OwnerLinked += owner => Dispatcher.BeginInvoke(() =>
            Ui.ShowOsd(Loc.T("telegram.linked", owner.Shown), OsdKind.Success, null, OsdLook.Of("Send24")));
    }

    private void StartTelegram()
    {
        try
        {
            Telegram.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Telegram: no se ha podido iniciar", ex);
        }
    }

    /// <summary>An important mail, to the owner by Telegram (Opciones › Cuentas › Telegram › Avisos).</summary>
    private void TellMailByTelegram(Core.Mail.MailArrived arrived)
    {
        if (!arrived.Important || arrived.Account.Notify == Core.Mail.MailNotify.None || !Telegram.Settings.Notices.ImportantMail) return;
        var m = arrived.Mail;
        var subject = string.IsNullOrWhiteSpace(m.Subject) ? Loc.T("mail.noSubject") : m.Subject;
        Telegram.SendToOwner($"✉️ {Loc.T("telegram.notice.mail", m.From.Shown)}\n{subject}" + (m.Snippet.Length > 0 ? "\n\n" + m.Snippet : string.Empty));
    }
}
