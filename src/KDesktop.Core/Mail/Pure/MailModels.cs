using System.Text.Json.Serialization;

namespace KDesktop.Core.Mail;

/// <summary>How an account signs in: a password (an app password, mostly) or the OAuth of a connected account.</summary>
public enum MailAuth { Password, OAuth }

/// <summary>What K-Desktop does with an account: everything, or only send (the K-Desktop account for reports).</summary>
public enum MailUse { All, SendOnly }

/// <summary>Which mail is told when it arrives.</summary>
public enum MailNotify { Important, All, None }

/// <summary>
/// A mail account (Mail\settings.json): the address, its servers, how it signs in (the password encrypted with DPAPI, or
/// a connected account of Accounts\), what it is for and the folders watched.
/// </summary>
public sealed class MailAccountSettings
{
    public string Id { get; set; } = "mail-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>The name the user gives it («Personal», «Trabajo»).</summary>
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    /// <summary>The name shown in the mails sent.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;

    /// <summary>The user name for the servers (usually the address).</summary>
    public string User { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>465: TLS from the start; 587: STARTTLS.</summary>
    public int SmtpPort { get; set; } = 587;

    public MailAuth Auth { get; set; } = MailAuth.Password;

    /// <summary>The password, encrypted (<see cref="Platform.Secrets"/>).</summary>
    public string PasswordProtected { get; set; } = string.Empty;

    /// <summary>With OAuth: the account of Accounts\.</summary>
    public string? OAuthAccountId { get; set; }

    public MailUse Use { get; set; } = MailUse.All;

    public MailNotify Notify { get; set; } = MailNotify.Important;

    /// <summary>Folders watched besides the inbox.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>With «solo enviar»: who it may send to (empty: only its own address and the user's).</summary>
    public List<string> AllowedRecipients { get; set; } = [];

    /// <summary>Mark as read the mails the assistant reads (off: they stay as they were).</summary>
    public bool MarkReadByAssistant { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonIgnore] public string Display => Name.Length > 0 ? $"{Name} ({Address})" : Address;
}

/// <summary>What makes a mail important (it is told): senders, domains and words in the subject.</summary>
public sealed class MailRulesSettings
{
    /// <summary>Addresses or parts of them («ana@empresa.es», «@banco.es», «Hacienda»).</summary>
    public List<string> Senders { get; set; } = [];

    /// <summary>Words in the subject («factura», «urgente»).</summary>
    public List<string> Keywords { get; set; } = [];
}

/// <summary>Everything of the mail's options.</summary>
public sealed class MailSettings
{
    public List<MailAccountSettings> Accounts { get; set; } = [];

    public MailRulesSettings Important { get; set; } = new();

    /// <summary>Days of headers kept to look for mails without asking the server.</summary>
    public int KeepDays { get; set; } = 30;

    /// <summary>The assistants may send directly (always asking first); off: only drafts.</summary>
    public bool AssistantMaySend { get; set; } = true;
}

/// <summary>A mail's address with its name.</summary>
public sealed record MailPerson(string Name, string Address)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Address : $"{Name} <{Address}>";

    [JsonIgnore] public string Shown => string.IsNullOrWhiteSpace(Name) ? Address : Name;
}

/// <summary>An attachment, as the list of a mail says it (its bytes are asked for when needed).</summary>
public sealed record MailAttachment(string Name, long Size, string ContentType, int Index);

/// <summary>
/// The header of a mail, as kept in the cache: enough to list it, look for it and tell it. Its <see cref="Id"/> is the
/// account, the folder, the folder's UIDVALIDITY and the UID («mail-1|INBOX|1234|5678»).
/// </summary>
public sealed record MailHeader
{
    public string Id { get; init; } = string.Empty;
    public string AccountId { get; init; } = string.Empty;
    public string Folder { get; init; } = "INBOX";
    public uint Uid { get; init; }
    public uint UidValidity { get; init; }
    public string? MessageId { get; init; }
    public MailPerson From { get; init; } = new(string.Empty, string.Empty);
    public IReadOnlyList<MailPerson> To { get; init; } = [];
    public IReadOnlyList<MailPerson> Cc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public DateTimeOffset Date { get; init; }
    public string Snippet { get; init; } = string.Empty;
    public bool Read { get; init; }
    public bool Flagged { get; init; }
    public IReadOnlyList<MailAttachment> Attachments { get; init; } = [];

    public static string MakeId(string account, string folder, uint validity, uint uid) => $"{account}|{folder}|{validity}|{uid}";

    /// <summary>The parts of an id; null when it is not one.</summary>
    public static (string Account, string Folder, uint Validity, uint Uid)? ParseId(string id)
    {
        var parts = id.Split('|');
        if (parts.Length < 4 || !uint.TryParse(parts[^2], out var validity) || !uint.TryParse(parts[^1], out var uid)) return null;
        return (parts[0], string.Join('|', parts[1..^2]), validity, uid);
    }
}

/// <summary>A mail read whole: its header and its text (clean), and the attachments.</summary>
public sealed record MailContent(MailHeader Header, string Text, string? Html, IReadOnlyList<MailPerson> ReplyTo);

/// <summary>A mail to send or leave as a draft.</summary>
public sealed record MailDraft(string AccountId, IReadOnlyList<string> To, string Subject, string Body,
    IReadOnlyList<string>? Cc = null, IReadOnlyList<string>? Attachments = null, string? InReplyTo = null);
