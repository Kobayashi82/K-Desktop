using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Mail;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// The mail in K-Desktop's process (mail, phases 1–3): the accounts are watched, what arrives is told (all, the
/// important ones or nothing, per account) with a notification that opens the mail on the web or adds an invitation to
/// the calendar, the events of «Llega un correo» fire and «Enviar correo» sends. Works with the assistant off.
/// </summary>
public partial class App
{
    public MailService Mail { get; private set; } = null!;

    private void CreateMail()
    {
        Mail = new MailService(MailService.DefaultFolder, Secrets.Protect, Secrets.Unprotect, Accounts);
        Mail.Arrived += arrived => Dispatcher.BeginInvoke(() => ShowNewMail(arrived));
        Events.Mail = Mail;
        Engine.Mail = Mail;
    }

    private void StartMail()
    {
        try
        {
            Mail.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Correo: no se ha podido iniciar", ex);
        }
    }

    private void ShowNewMail(MailArrived arrived)
    {
        if (arrived.Important) Achievements.Increment(Core.Achievements.Stat.MailImportant);
        TellMailByTelegram(arrived);
        var notify = arrived.Account.Notify;
        if (notify == MailNotify.None || (notify == MailNotify.Important && !arrived.Important)) return;
        var m = arrived.Mail;
        var invitation = m.Attachments.Any(a => a.Name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) || a.ContentType == "text/calendar");
        var text = (string.IsNullOrWhiteSpace(m.Subject) ? Loc.T("mail.noSubject") : m.Subject)
                   + (m.Snippet.Length > 0 ? "\n" + m.Snippet : string.Empty)
                   + (invitation ? "\n" + Loc.T("mail.notice.invitation") : string.Empty);
        ShowMailNotice(arrived, text, invitation);
    }

    /// <summary>«Probar un aviso» (Opciones › Correo): a made-up mail, told as the options say.</summary>
    public void ShowMailNoticePreview()
    {
        var mail = new MailHeader { From = new MailPerson(Loc.T("mailOptions.testFrom"), "ana@example.com"), Subject = Loc.T("mailOptions.testSubject") };
        ShowMailNotice(new MailArrived(mail, true, new MailAccountSettings { ImapHost = "imap.gmail.com" }), mail.Subject, false);
    }

    /// <summary>
    /// A new mail, told as Opciones › Correo › Avisos says: a notification (it opens the mail), an OSD, or said by an
    /// assistant on the desktop (else an OSD). An invitation always goes as a notification: clicking it adds it.
    /// </summary>
    private void ShowMailNotice(MailArrived arrived, string text, bool invitation)
    {
        var m = arrived.Mail;
        var subject = string.IsNullOrWhiteSpace(m.Subject) ? Loc.T("mail.noSubject") : m.Subject;
        var style = invitation ? Core.Calendar.ReminderStyle.Notification : Mail.Settings.NoticeStyle;
        if (style == Core.Calendar.ReminderStyle.Assistant)
        {
            var id = Mail.Settings.NoticeAssistant ?? Core.Assistant.AssistantTargeting.KDesktop;
            if (_assistant is not null && IsAssistant(id) && IsOnDesktop(id))
            {
                SayToAssistant(id, Loc.T("mail.notice.say", m.From.Shown, subject), true);
                return;
            }
            style = Core.Calendar.ReminderStyle.Osd;
        }
        if (style == Core.Calendar.ReminderStyle.Osd)
        {
            Ui.ShowOsd($"{m.From.Shown}: {subject}", OsdKind.Info, TimeSpan.FromSeconds(8), OsdLook.Of("Mail24"));
            return;
        }
        _ = Ui.ShowNotificationAsync(new Notification(m.From.Shown, text, "kd:Mail24:blue", arrived.Important ? NotifyColor.Gold : NotifyColor.Blue,
            ShowNotificationAction.GlobalSound, TimeSpan.FromSeconds(12), null,
            invitation ? () => _ = AddInvitationAsync(m) : () => OpenWebMail(arrived.Account)));
    }

    private async Task AddInvitationAsync(MailHeader mail)
    {
        try
        {
            var change = await MailAssistant.AddInvitationAsync(Mail, Calendar, mail.Id);
            if (change is not null) Achievements.Increment(Core.Achievements.Stat.InvitationsAdded);
            Ui.ShowOsd(change is null ? Loc.T("mail.notice.noInvitation") : Loc.T("mail.notice.invitationAdded", change.Result?.Title ?? string.Empty),
                change is null ? OsdKind.Warning : OsdKind.Success, null, OsdLook.Of("CalendarAdd24"));
        }
        catch (Exception ex) when (ex is MailException or Core.Calendar.CalendarException or System.IO.IOException)
        {
            Ui.ShowOsd(ex.Message, OsdKind.Warning);
        }
    }

    /// <summary>Opens the account's mail on the provider's web (Gmail, Outlook); the others, the default mail program.</summary>
    public static void OpenWebMail(MailAccountSettings account)
    {
        var host = account.ImapHost.ToLowerInvariant();
        var url = host.Contains("gmail", StringComparison.Ordinal) ? "https://mail.google.com/mail/u/0/#inbox"
            : host.Contains("office365", StringComparison.Ordinal) || host.Contains("outlook", StringComparison.Ordinal) ? "https://outlook.live.com/mail/"
            : host.Contains("me.com", StringComparison.Ordinal) ? "https://www.icloud.com/mail"
            : host.Contains("yahoo", StringComparison.Ordinal) ? "https://mail.yahoo.com/"
            : "mailto:";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Correo: no se ha podido abrir {url}: {ex.Message}");
        }
    }

    /// <summary>An assistant's request to the mail (mail_*), answered off the UI thread.</summary>
    private void OnMail(Core.Assistant.AssistantSupervisor supervisor, Core.Assistant.Pipe.MailMessage request) =>
        _ = Task.Run(async () =>
        {
            MailAnswer answer;
            try
            {
                answer = await MailAssistant.HandleAsync(Mail, Calendar, request.Request).ConfigureAwait(false);
                if (answer.Ok && request.Request.Do is "send" or "draft" or "move")
                    Log.Info($"Asistente: correo «{request.Request.Do}» ({request.Assistant ?? "módulo"})");
            }
            catch (Exception ex)
            {
                Log.Error("Asistente: error en el correo", ex);
                answer = new MailAnswer(false, Reason: "error", Message: ex.Message);
            }
            supervisor.Send(new Core.Assistant.Pipe.MailDoneMessage(request.Id, answer));
        });
}
