using System.Xml.Linq;

namespace KDesktop.Core.Mail;

/// <summary>The servers of a mail account.</summary>
/// <param name="Provider">«gmail», «outlook», «icloud»…; null: found by autoconfiguration.</param>
/// <param name="OAuth">The OAuth provider it can use.</param>
/// <param name="NeedsOAuth">The password does not work any more and K-Desktop cannot connect it (Outlook.com, Hotmail).</param>
public sealed record MailServers(string ImapHost, int ImapPort, string SmtpHost, int SmtpPort, string? Provider = null,
    Accounts.AccountProvider? OAuth = null, bool NeedsOAuth = false);

/// <summary>
/// The servers of an address (mail, phase 1.1): a list of the usual ones (Gmail, Outlook, iCloud, Yahoo…) and, for the
/// rest, Thunderbird's autoconfiguration (the domain's own, then Mozilla's database). Otherwise the user writes them.
/// </summary>
public static class MailAutoConfig
{
    private static readonly (string[] Domains, MailServers Servers)[] Known =
    [
        (["gmail.com", "googlemail.com"], new("imap.gmail.com", 993, "smtp.gmail.com", 465, "gmail", Accounts.AccountProvider.Google)),
        (["outlook.com", "hotmail.com", "hotmail.es", "live.com", "live.es", "msn.com", "outlook.es"],
            new("outlook.office365.com", 993, "smtp-mail.outlook.com", 587, "outlook", NeedsOAuth: true)),
        (["icloud.com", "me.com", "mac.com"], new("imap.mail.me.com", 993, "smtp.mail.me.com", 587, "icloud")),
        (["yahoo.com", "yahoo.es", "ymail.com"], new("imap.mail.yahoo.com", 993, "smtp.mail.yahoo.com", 465, "yahoo")),
        (["aol.com"], new("imap.aol.com", 993, "smtp.aol.com", 465, "aol")),
        (["gmx.com", "gmx.es", "gmx.net", "gmx.de"], new("imap.gmx.com", 993, "mail.gmx.com", 587, "gmx")),
        (["zoho.com", "zohomail.com"], new("imap.zoho.com", 993, "smtp.zoho.com", 465, "zoho")),
        (["yandex.com", "yandex.ru"], new("imap.yandex.com", 993, "smtp.yandex.com", 465, "yandex")),
        (["proton.me", "protonmail.com"], new("127.0.0.1", 1143, "127.0.0.1", 1025, "proton")),
        (["fastmail.com", "fastmail.fm"], new("imap.fastmail.com", 993, "smtp.fastmail.com", 465, "fastmail")),
    ];

    public static string? DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at < 0 || at == address.Length - 1 ? null : address[(at + 1)..].Trim().ToLowerInvariant();
    }

    /// <summary>The servers of a known provider, or null.</summary>
    public static MailServers? Lookup(string address)
    {
        var domain = DomainOf(address);
        if (domain is null) return null;
        foreach (var (domains, servers) in Known)
            if (domains.Contains(domain)) return servers;
        return null;
    }

    /// <summary>Known, or found by autoconfiguration (the domain's own file, then Mozilla's database); null when nothing says.</summary>
    public static async Task<MailServers?> FindAsync(string address, HttpClient http, CancellationToken cancel = default)
    {
        if (Lookup(address) is { } known) return known;
        var domain = DomainOf(address);
        if (domain is null) return null;
        string[] urls =
        [
            $"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(address)}",
            $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml",
            $"https://autoconfig.thunderbird.net/v1.1/{Uri.EscapeDataString(domain)}",
        ];
        foreach (var url in urls)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var xml = await http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
                if (Parse(xml) is { } found) return found;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
            {
                // the next one
            }
        }
        return null;
    }

    /// <summary>
    /// A Thunderbird autoconfiguration file → the servers: the first IMAP with SSL (or STARTTLS) and the first SMTP. Null
    /// when it has none of those (POP only, or plain text).
    /// </summary>
    public static MailServers? Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        XElement? Pick(string direction, string type) => doc.Descendants(direction + "Server")
            .Where(s => (string?)s.Attribute("type") == type)
            .OrderBy(s => (string?)s.Element("socketType") == "SSL" ? 0 : (string?)s.Element("socketType") == "STARTTLS" ? 1 : 2)
            .FirstOrDefault(s => (string?)s.Element("socketType") is "SSL" or "STARTTLS");
        var imap = Pick("incoming", "imap");
        var smtp = Pick("outgoing", "smtp");
        if (imap is null || smtp is null) return null;
        var imapHost = (string?)imap.Element("hostname");
        var smtpHost = (string?)smtp.Element("hostname");
        if (string.IsNullOrWhiteSpace(imapHost) || string.IsNullOrWhiteSpace(smtpHost)) return null;
        return new MailServers(imapHost, (int?)imap.Element("port") ?? 993, smtpHost, (int?)smtp.Element("port") ?? 587);
    }
}

/// <summary>Which mails are important (mail, phase 2.2): their sender or domain, or a word in their subject.</summary>
public static class MailRules
{
    public static bool IsImportant(MailHeader mail, MailRulesSettings rules)
    {
        var from = $"{mail.From.Name} <{mail.From.Address}>".ToLowerInvariant();
        foreach (var sender in rules.Senders.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0))
            if (from.Contains(sender, StringComparison.Ordinal)) return true;
        var subject = Fold(mail.Subject);
        foreach (var word in rules.Keywords.Select(Fold).Where(w => w.Length > 0))
            if (subject.Contains(word, StringComparison.Ordinal)) return true;
        return false;
    }

    public static string Fold(string text) =>
        new string(text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    /// <summary>Whether a mail fits words (all of them) in its sender, subject or snippet; empty: any.</summary>
    public static bool Matches(MailHeader mail, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var hay = Fold($"{mail.From.Name} {mail.From.Address} {mail.Subject} {mail.Snippet} {string.Join(' ', mail.Attachments.Select(a => a.Name))}");
        return Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => hay.Contains(w, StringComparison.Ordinal));
    }
}
