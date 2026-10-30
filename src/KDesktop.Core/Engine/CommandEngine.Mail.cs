using KDesktop.Core.Localization;
using KDesktop.Core.Mail;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    /// <summary>The mail («Enviar correo»); set by K-Desktop at start.</summary>
    public MailService? Mail { get; set; }

    private async Task MailSendAsync(MailSendAction a, ExecutionContext ctx)
    {
        if (Mail is not { } mail) throw new InvalidOperationException(Loc.T("mail.notReady"));
        var accounts = mail.Accounts;
        var account = (a.Account.Trim().Length > 0 ? mail.Account(a.Account.Trim()) : null) ?? (accounts.Count > 0 ? accounts[0] : null)
                      ?? throw new InvalidOperationException(Loc.T("mail.error.noAccount"));
        static List<string> Split(string text) =>
            [.. text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        var to = Split(TextVariables.Expand(a.To));
        if (to.Count == 0) throw new InvalidOperationException(Loc.T("mail.error.noRecipient"));
        var files = TextVariables.Expand(a.Attachments).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.Trim('"')).Where(f => f.Length > 0).ToList();
        var draft = new MailDraft(account.Id, to, TextVariables.Expand(a.Subject), TextVariables.Expand(a.Body), Attachments: files);
        ctx.Token.ThrowIfCancellationRequested();
        if (a.DraftOnly)
        {
            await mail.SaveDraftAsync(draft, ctx.Token).ConfigureAwait(false);
            _ui.ShowOsd(Loc.T("osd.mailDraft", string.Join(", ", to)), OsdKind.Success, null, OsdLook.Of("DraftsFilled24"));
        }
        else
        {
            await mail.SendAsync(draft, ctx.Token).ConfigureAwait(false);
            _ui.ShowOsd(Loc.T("osd.mailSent", string.Join(", ", to)), OsdKind.Success, null, OsdLook.Of("MailCheckmark24"));
        }
        Gave(a, string.Join(", ", to));
    }
}
