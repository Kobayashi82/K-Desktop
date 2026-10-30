using System.Net.Sockets;
using KDesktop.Core.Accounts;
using KDesktop.Core.Localization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace KDesktop.Core.Mail;

/// <summary>A mail account could not be used: the message says why in the user's words.</summary>
public sealed class MailException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One connection to an account's servers with MailKit (mail, phase 1.2): IMAP to read, look for, move and watch (IDLE),
/// SMTP to send; with the password (an app password) or XOAUTH2 with a connected account's token.
/// </summary>
public sealed class MailConnection(MailAccountSettings account, Func<string, string?> unprotect, AccountService? accounts) : IAsyncDisposable
{
    public MailAccountSettings Account { get; } = account;

    public ImapClient Imap { get; } = new() { Timeout = 60_000 };

    private string User => Account.User.Length > 0 ? Account.User : Account.Address;

    /// <summary>Connects to IMAP and signs in.</summary>
    public async Task<ImapClient> OpenImapAsync(CancellationToken cancel)
    {
        if (Imap.IsConnected && Imap.IsAuthenticated) return Imap;
        try
        {
            if (!Imap.IsConnected)
                await Imap.ConnectAsync(Account.ImapHost, Account.ImapPort, Account.ImapPort == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancel).ConfigureAwait(false);
            await SignInAsync(Imap, cancel).ConfigureAwait(false);
            return Imap;
        }
        catch (Exception ex) when (Translate(ex) is { } friendly)
        {
            throw friendly;
        }
    }

    /// <summary>Sends a message by SMTP (a connection of its own, closed afterwards).</summary>
    public async Task SendAsync(MimeMessage message, CancellationToken cancel)
    {
        using var smtp = new SmtpClient { Timeout = 60_000 };
        try
        {
            await smtp.ConnectAsync(Account.SmtpHost, Account.SmtpPort, Account.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancel).ConfigureAwait(false);
            await SignInAsync(smtp, cancel).ConfigureAwait(false);
            await smtp.SendAsync(message, cancel).ConfigureAwait(false);
            await smtp.DisconnectAsync(true, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex) is { } friendly)
        {
            throw friendly;
        }
    }

    /// <summary>Only checks that SMTP takes the account (Opciones › Correo › Probar).</summary>
    public async Task TestSmtpAsync(CancellationToken cancel)
    {
        using var smtp = new SmtpClient { Timeout = 30_000 };
        try
        {
            await smtp.ConnectAsync(Account.SmtpHost, Account.SmtpPort, Account.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancel).ConfigureAwait(false);
            await SignInAsync(smtp, cancel).ConfigureAwait(false);
            await smtp.DisconnectAsync(true, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (Translate(ex) is { } friendly)
        {
            throw friendly;
        }
    }

    private async Task SignInAsync(MailKit.MailService client, CancellationToken cancel)
    {
        if (client.IsAuthenticated) return;
        if (Account.Auth == MailAuth.OAuth)
        {
            if (accounts is null || Account.OAuthAccountId is null) throw new MailException(Loc.T("mail.error.noAccount"));
            var token = await accounts.AccessTokenAsync(Account.OAuthAccountId, AccountService.Resource.Mail, cancel).ConfigureAwait(false);
            await client.AuthenticateAsync(new SaslMechanismOAuth2(User, token), cancel).ConfigureAwait(false);
            return;
        }
        var password = unprotect(Account.PasswordProtected);
        if (string.IsNullOrEmpty(password)) throw new MailException(Loc.T("mail.error.passwordLost"));
        await client.AuthenticateAsync(User, password, cancel).ConfigureAwait(false);
    }

    /// <summary>What went wrong, in words («contraseña de aplicación incorrecta», «IMAP desactivado en Gmail»); null: not ours to say.</summary>
    public MailException? Translate(Exception ex)
    {
        var gmail = Account.ImapHost.Contains("gmail", StringComparison.OrdinalIgnoreCase);
        var text = ex.Message;
        return ex switch
        {
            MailException => null,
            AccountDisconnectedException => new MailException(ex.Message, ex),
            AuthenticationException when gmail && text.Contains("Application-specific password", StringComparison.OrdinalIgnoreCase) =>
                new MailException(Loc.T("mail.error.gmailAppPassword"), ex),
            AuthenticationException when text.Contains("IMAP", StringComparison.OrdinalIgnoreCase) && text.Contains("disabled", StringComparison.OrdinalIgnoreCase) =>
                new MailException(Loc.T(gmail ? "mail.error.gmailImapOff" : "mail.error.imapOff"), ex),
            AuthenticationException when Account.Auth == MailAuth.Password && Account.ImapHost.Contains("office365", StringComparison.OrdinalIgnoreCase) =>
                new MailException(Loc.T("mail.error.outlookNeedsOAuth"), ex),
            AuthenticationException => new MailException(Loc.T(Account.Auth == MailAuth.OAuth ? "mail.error.oauthRefused" : "mail.error.password"), ex),
            SslHandshakeException => new MailException(Loc.T("mail.error.tls", Account.ImapHost), ex),
            SocketException or IOException => new MailException(Loc.T("mail.error.connect", ex is SocketException ? Account.ImapHost : text), ex),
            ImapProtocolException or SmtpProtocolException => new MailException(Loc.T("mail.error.protocol", text), ex),
            SmtpCommandException smtp => new MailException(Loc.T("mail.error.smtp", smtp.Message), ex),
            ImapCommandException imap => new MailException(Loc.T("mail.error.imap", imap.Message), ex),
            _ => null,
        };
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Imap.IsConnected) await Imap.DisconnectAsync(true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ImapProtocolException or OperationCanceledException)
        {
            // closing anyway
        }
        Imap.Dispose();
    }
}
