using System.Text.Json.Serialization;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>Someone in Telegram: its id (the identity, never the name or the @user), the name shown and the @user.</summary>
public sealed record TelegramUser(long Id, string Name, string? Username)
{
    [JsonIgnore] public string Shown => Username is { Length: > 0 } u ? $"{Name} (@{u})" : Name;
}

public enum TelegramChatKind { Private, Group, Channel }

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
    TelegramFile? File = null);

/// <summary>What a connection can do, so the options and the tools explain it instead of failing.</summary>
public sealed record TelegramCapabilities(bool CanWriteFirst, bool CanReadGroups, long MaxUpload, long MaxDownload, bool CanEdit, bool CanDelete)
{
    /// <summary>A bot: it only writes to who wrote to it first; files up to 50 MB out and 20 MB in.</summary>
    public static TelegramCapabilities Bot { get; } = new(false, false, 50L << 20, 20L << 20, true, true);
}

/// <summary>How a file is sent: as it is, as a photo (shown) or as a voice note.</summary>
public enum TelegramFileKind { Document, Photo, Voice }

/// <summary>A failure of Telegram's API, with its code (401: the token is wrong; 429: too fast, wait <see cref="RetryAfter"/>).</summary>
public sealed class TelegramException(string message, int code = 0, TimeSpan? retryAfter = null) : Exception(message)
{
    public int Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The token is not valid (revoked or mistyped): trying again does not help.</summary>
    public bool BadToken => Code is 401 or 404;
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

    /// <summary>Sends a text; returns the message's id.</summary>
    Task<int> SendTextAsync(long chat, string text, CancellationToken cancel);

    Task<int> SendFileAsync(long chat, TelegramFileKind kind, string path, string? caption, CancellationToken cancel);

    /// <summary>«Escribiendo…» and the like (typing, upload_photo, record_voice…).</summary>
    Task SendChatActionAsync(long chat, string action, CancellationToken cancel);

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

/// <summary>Everything of Telegram's options. Off and closed by default: nothing works until a bot and an owner are set.</summary>
public sealed class TelegramSettings
{
    public TelegramBotSettings? Bot { get; set; }
    public TelegramOwner? Owner { get; set; }
    public TelegramNotices Notices { get; set; } = new();
}

/// <summary>How the connection is now.</summary>
public enum TelegramState { Off, Connecting, Connected, Problem }
