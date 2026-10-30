using KDesktop.Core.Mail;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine.Events;

/// <summary>«Llega un correo» (mail, phase 2.4): the mail service says what arrives; the events that wait for it fire.</summary>
public sealed partial class EventService
{
    /// <summary>The mail the events of «Llega un correo» look at; set by K-Desktop at start.</summary>
    public MailService? Mail
    {
        get;
        set
        {
            if (field is not null) field.Arrived -= OnMailArrived;
            field = value;
            if (field is not null) field.Arrived += OnMailArrived;
        }
    }

    private void OnMailArrived(MailArrived arrived)
    {
        List<(EventRule Rule, MailTrigger Trigger)> rules;
        lock (_lock)
        {
            if (_disposed) return;
            rules = [.. Rules<MailTrigger>().Where(r => r.Trigger.Matches(arrived.Mail, arrived.Important))];
        }
        foreach (var (rule, trigger) in rules) _ = FireMailAsync(rule, trigger, arrived);
    }

    private async Task FireMailAsync(EventRule rule, MailTrigger trigger, MailArrived arrived)
    {
        var paths = new List<string>();
        if (trigger.SaveAttachmentsTo.Trim().Length > 0 && arrived.Mail.Attachments.Count > 0 && Mail is { } mail)
        {
            try
            {
                var folder = Environment.ExpandEnvironmentVariables(TextVariables.Expand(trigger.SaveAttachmentsTo.Trim()));
                paths.AddRange(await mail.SaveAttachmentsAsync(arrived.Mail.Id, folder, trigger.AttachmentFilter).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is MailException or IOException or UnauthorizedAccessException or MailKit.ServiceNotConnectedException)
            {
                Log.Warn($"Evento «{rule.Name}»: no se han podido guardar los adjuntos: {ex.Message}");
            }
        }
        Log.Info($"Eventos: correo de {arrived.Mail.From.Address} → «{rule.Name}»");
        Fire(rule, false, Variables(arrived, paths));
    }

    /// <summary>{correo_de}, {correo_asunto}, {correo_fragmento}, {correo_adjuntos}, {correo_rutas}, {correo_cuenta} (and {mail_from}…); {resultado}: the subject.</summary>
    public static CommandVariables Variables(MailArrived arrived, IReadOnlyList<string> saved)
    {
        var v = new CommandVariables();
        var m = arrived.Mail;
        void Set(string es, string en, string value)
        {
            v.SetResult(value, es);
            v.SetResult(value, en);
        }
        Set("correo_de", "mail_from", m.From.ToString());
        Set("correo_fragmento", "mail_snippet", m.Snippet);
        Set("correo_adjuntos", "mail_attachments", string.Join(", ", m.Attachments.Select(a => a.Name)));
        Set("correo_rutas", "mail_paths", string.Join("\n", saved));
        Set("correo_cuenta", "mail_account", arrived.Account.Address);
        Set("correo_asunto", "mail_subject", m.Subject); // last: {resultado} is the subject
        return v;
    }
}
