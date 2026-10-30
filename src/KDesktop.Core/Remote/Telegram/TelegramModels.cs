using System.Text.Json.Serialization;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>Someone in Telegram: its id (the identity, never the name or the @user), the name shown and the @user.</summary>
public sealed record TelegramUser(long Id, string Name, string? Username, string? Phone = null)
{
    [JsonIgnore] public string Shown => Username is { Length: > 0 } u ? $"{Name} (@{u})" : Name;
}

public enum TelegramChatKind { Private, Group, Channel }

/// <summary>Which connection: the bot (Opciones › Cuentas › Telegram Bot) or the account with a number (› Telegram).</summary>
public enum TelegramVia { Bot, Account }

/// <summary>What a message brings besides its text.</summary>
public enum TelegramContent { Text, Voice, Photo, Document, Other }

/// <summary>A file of a message, as Telegram gives it (downloaded only when asked).</summary>
public sealed record TelegramFile(string Id, string? Name, string? MimeType, long Size);

/// <summary>A message that arrived (a bot's update, or later an account's), in the same shape whatever the connection.</summary>
public sealed record TelegramIncoming(
    long UpdateId,
    long ChatId,
    TelegramChatKind ChatKind,
    string? ChatTitle,
    int MessageId,
    TelegramUser From,
    DateTimeOffset Date,
    string Text,
    TelegramContent Content = TelegramContent.Text,
    TelegramFile? File = null,
    long? ReplyToUserId = null,
    string? CallbackId = null,
    string? CallbackData = null,
    bool Forwarded = false,
    TelegramVia Via = TelegramVia.Bot,
    int? ReplyToMessageId = null)
{
    /// <summary>A button of a message pressed (<see cref="CallbackData"/>), not a message.</summary>
    public bool IsButton => CallbackId is not null;
}

/// <summary>A message of a chat lately (telegram_read): who said it, when, and whether the bot sent it.</summary>
public sealed record TelegramLine(long Chat, string From, DateTimeOffset At, string Text, bool Sent);

/// <summary>A button under a message; pressed, it comes back as <see cref="TelegramIncoming.CallbackData"/>.</summary>
public sealed record TelegramButton(string Text, string Data);

/// <summary>What a connection can do, so the options and the tools explain it instead of failing.</summary>
public sealed record TelegramCapabilities(bool CanWriteFirst, bool CanReadGroups, long MaxUpload, long MaxDownload, bool CanEdit, bool CanDelete, bool CanButtons = true)
{
    /// <summary>A bot: it only writes to who wrote to it first; files up to 50 MB out and 20 MB in.</summary>
    public static TelegramCapabilities Bot { get; } = new(false, false, 50L << 20, 20L << 20, true, true);

    /// <summary>
    /// An account with a number (phase 6): it writes first and reads groups, files up to 2 GB, but without buttons (they go
    /// as numbered lines, answered with the number or the word).
    /// </summary>
    public static TelegramCapabilities Account { get; } = new(true, true, 2000L << 20, 2000L << 20, true, true, CanButtons: false);
}

/// <summary>What only an account with a number does (phase 6.4): its contacts in Telegram, adding one by its number, logging out.</summary>
public interface ITelegramAgenda
{
    Task<IReadOnlyList<TelegramUser>> AgendaAsync(CancellationToken cancel);

    /// <summary>Adds someone to the account's contacts by their number; null when that number has no Telegram.</summary>
    Task<TelegramUser?> AddToAgendaAsync(string phone, string firstName, string lastName, CancellationToken cancel);

    /// <summary>Closes the session in Telegram's servers too.</summary>
    Task LogOutAsync(CancellationToken cancel);
}

/// <summary>How a file is sent: as it is, as a photo (shown) or as a voice note.</summary>
public enum TelegramFileKind { Document, Photo, Voice }

/// <summary>A failure of Telegram's API, with its code (401: the token is wrong; 429: too fast, wait <see cref="RetryAfter"/>).</summary>
public sealed class TelegramException(string message, int code = 0, TimeSpan? retryAfter = null) : Exception(message)
{
    public int Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The token is not valid (revoked or mistyped), or the account's session was closed: trying again does not help.</summary>
    public bool BadToken => Code is 401 or 404;

    /// <summary>Telegram limits the account (it wrote to too many who don't know it): it can only answer for a while.</summary>
    public bool Limited { get; init; }
}

/// <summary>
/// A connection to Telegram (telegram, phase 1: the Bot API; phase 6: an account with a number). Contacts, permissions,
/// actions and tools do not know which one is used.
/// </summary>
public interface ITelegramConnection : IDisposable
{
    TelegramCapabilities Capabilities { get; }

    /// <summary>Who it is (the bot's own name and @user).</summary>
    Task<TelegramUser> GetMeAsync(CancellationToken cancel);

    /// <summary>Waits for what arrives (long polling) and gives it; each call confirms the previous ones.</summary>
    Task<IReadOnlyList<TelegramIncoming>> ReceiveAsync(CancellationToken cancel);

    /// <summary>Sends a text, with rows of buttons under it if given; returns the message's id.</summary>
    Task<int> SendTextAsync(long chat, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken cancel);

    /// <summary>Tells Telegram a button was handled (it stops spinning), with a short text shown over the chat.</summary>
    Task AnswerButtonAsync(string callbackId, string? text, CancellationToken cancel);

    Task<int> SendFileAsync(long chat, TelegramFileKind kind, string path, string? caption, CancellationToken cancel);

    /// <summary>«Escribiendo…» and the like (typing, upload_photo, record_voice…).</summary>
    Task SendChatActionAsync(long chat, string action, CancellationToken cancel);

    /// <summary>Changes a message's text (its buttons go).</summary>
    Task EditTextAsync(long chat, int message, string text, CancellationToken cancel);

    Task DeleteAsync(long chat, int message, CancellationToken cancel);

    Task DownloadAsync(string fileId, string destination, CancellationToken cancel);
}

/// <summary>The bot (Telegram\settings.json): its token encrypted with DPAPI, and who it is.</summary>
public sealed class TelegramBotSettings
{
    public string TokenProtected { get; set; } = string.Empty;
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// The account with a number (Telegram\Account\settings.json; phase 6): the application it connects with (api_id and
/// api_hash of my.telegram.org, the hash encrypted with DPAPI), the number it logged in with and who it is. The session goes
/// apart, encrypted too (account.session).
/// </summary>
public sealed class TelegramAccountSettings
{
    public int ApiId { get; set; }
    public string ApiHashProtected { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    [JsonIgnore] public string Shown => Username.Length > 0 ? $"{Name} (@{Username})" : Name;
}

/// <summary>The owner: the one person the bot works for (linked with a code, as the phone is paired).</summary>
public sealed class TelegramOwner
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
    public DateTimeOffset Linked { get; set; }

    [JsonIgnore] public string Shown => Username is { Length: > 0 } u ? $"{Name} (@{u})" : Name;
}

/// <summary>What K-Desktop tells the owner by Telegram by itself.</summary>
public sealed class TelegramNotices
{
    /// <summary>The important mails (Opciones › Correo says which are).</summary>
    public bool ImportantMail { get; set; } = true;
}

/// <summary>
/// What someone who writes may do (telegram, phase 2). It is the level of whoever writes that counts, not the chat's or
/// the group's.
/// </summary>
public enum TelegramLevel
{
    /// <summary>Nothing: their messages are dropped.</summary>
    Ignore,

    /// <summary>Talk with the assistant, without tools.</summary>
    ChatOnly,

    /// <summary>Run the commands of a list (without AI).</summary>
    ChosenCommands,

    /// <summary>Talk + the tools that only read.</summary>
    ReadOnlyTools,

    /// <summary>Talk + a list of tools and commands.</summary>
    ChosenTools,

    /// <summary>As on the PC (the confirmations still go to the owner).</summary>
    LikeOwner,
}

/// <summary>A contact: known by its Telegram id; the name and the @user are only shown, and refreshed when they change.</summary>
public sealed class TelegramContact
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }

    /// <summary>Their number, when they were added with it or come from the account's contacts (only shown).</summary>
    public string? Phone { get; set; }

    public TelegramLevel Level { get; set; } = TelegramLevel.ChatOnly;

    /// <summary>K-Desktop may write to them (when asked to).</summary>
    public bool MayWrite { get; set; } = true;

    /// <summary>With <see cref="TelegramLevel.ChosenCommands"/> or <see cref="TelegramLevel.ChosenTools"/>: the ids of the commands.</summary>
    public List<string> Commands { get; set; } = [];

    /// <summary>With <see cref="TelegramLevel.ChosenTools"/>: the names of the tools.</summary>
    public List<string> Tools { get; set; } = [];

    /// <summary>Who answers them (an assistant's id); null: the one of «Quién contesta» for everybody.</summary>
    public string? Assistant { get; set; }

    /// <summary>How the assistant answers them (phase 5.2); null: as for everybody.</summary>
    public TelegramAnswerKind? Answer { get; set; }

    public DateTimeOffset Added { get; set; }

    /// <summary>The last time they wrote (null: never; then writing to them counts in the hourly limit).</summary>
    public DateTimeOffset? LastWrote { get; set; }

    [JsonIgnore] public string Shown => Username is { Length: > 0 } u ? $"{Name} (@{u})" : Name;
}

/// <summary>Someone who wrote and is waiting for the owner's decision, or blocked.</summary>
public sealed class TelegramPerson
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
    public DateTimeOffset When { get; set; }

    /// <summary>What they wrote first (to decide).</summary>
    public string? Text { get; set; }

    /// <summary>Blocked: the contact it was (unblocked, it comes back as it was); null: it was waiting for a decision.</summary>
    public TelegramContact? Was { get; set; }

    [JsonIgnore] public string Shown => Username is { Length: > 0 } u ? $"{Name} (@{u})" : Name;
}

/// <summary>A group the bot is in: it only answers when mentioned or replied to, or never.</summary>
public sealed class TelegramGroup
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool Ignore { get; set; }
    public DateTimeOffset Seen { get; set; }
}

/// <summary>Everybody else (who is neither the owner nor a contact).</summary>
public sealed class TelegramOthers
{
    /// <summary>Their level; above «Solo charla» only counts with <see cref="RiskAccepted"/>.</summary>
    public TelegramLevel Level { get; set; } = TelegramLevel.Ignore;

    /// <summary>«Entiendo el riesgo», marked on purpose.</summary>
    public bool RiskAccepted { get; set; }

    /// <summary>Tell the owner (PC and Telegram) when someone new writes.</summary>
    public bool Notify { get; set; } = true;

    /// <summary>The level that counts: without the risk accepted, never above «Solo charla».</summary>
    [JsonIgnore] public TelegramLevel Effective => Level > TelegramLevel.ChatOnly && !RiskAccepted ? TelegramLevel.ChatOnly : Level;
}

public sealed class TelegramLimits
{
    /// <summary>Messages a minute from each contact (the owner has no limit).</summary>
    public int PerMinute { get; set; } = 6;

    /// <summary>Only the owner while a game (or anything) is at full screen.</summary>
    public bool OnlyOwnerWhileGaming { get; set; } = true;

    /// <summary>Messages an hour to contacts who never wrote.</summary>
    public int NewPerHour { get; set; } = 5;
}

/// <summary>Everything of Telegram's options. Off and closed by default: nothing works until a bot and an owner are set.</summary>
public sealed class TelegramSettings
{
    public TelegramBotSettings? Bot { get; set; }

    /// <summary>The account with a number (only in the account's settings; the bot's have <see cref="Bot"/>).</summary>
    public TelegramAccountSettings? Account { get; set; }

    public TelegramOwner? Owner { get; set; }
    public TelegramNotices Notices { get; set; } = new();
    public List<TelegramContact> Contacts { get; set; } = [];

    /// <summary>Who wrote and waits for a decision.</summary>
    public List<TelegramPerson> Pending { get; set; } = [];

    public List<TelegramPerson> Blocked { get; set; } = [];
    public List<TelegramGroup> Groups { get; set; } = [];
    public TelegramOthers Others { get; set; } = new();
    public TelegramLimits Limits { get; set; } = new();

    /// <summary>Who answers by Telegram (an assistant's id); null: the one on the screen (the last one talked to).</summary>
    public string? Assistant { get; set; }

    /// <summary>Under an answer, what it did («✓ Abrió Spotify»).</summary>
    public bool ShowDid { get; set; } = true;

    /// <summary>How the assistant answers (phase 5.2), unless a contact has their own.</summary>
    public TelegramAnswerKind Answer { get; set; } = TelegramAnswerKind.Same;

    /// <summary>A voice note is answered first with what was understood of it («🎤 …»).</summary>
    public bool ShowHeard { get; set; } = true;
}

/// <summary>How the assistant answers by Telegram (phase 5.2): as it was written to, in text, with its voice or both.</summary>
public enum TelegramAnswerKind
{
    /// <summary>A voice note is answered with voice, a text with text.</summary>
    Same,
    Text,
    Voice,
    Both,
}

/// <summary>How the connection is now.</summary>
public enum TelegramState { Off, Connecting, Connected, Problem }
