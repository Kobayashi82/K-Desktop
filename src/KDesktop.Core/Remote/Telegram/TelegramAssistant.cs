using System.Globalization;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>What an assistant asks of Telegram (telegram_send, telegram_read, telegram_send_file).</summary>
/// <param name="Do"><c>send</c>, <c>read</c>, <c>contacts</c> or <c>undo</c>.</param>
/// <param name="To">send: who (empty: the owner; a contact by name, @user or id). read: whose chat or which group (empty: the owner's).</param>
/// <param name="File">send: a full path, <c>screenshot</c> (the whole screen) or <c>last</c> (the last file that arrived by Telegram).</param>
/// <param name="Undo">undo: what <c>send</c> gave.</param>
/// <param name="Via"><c>bot</c> or <c>account</c> (phase 6); null: K-Desktop picks (<see cref="TelegramAssistant.Choose"/>).</param>
public sealed record TelegramRequest(string Do, string? To = null, string? Text = null, string? File = null, int Max = 20, string? Undo = null, string? Via = null);

/// <param name="Reason">When not done: <c>notReady</c>, <c>noRecipient</c>, <c>cantWrite</c>, <c>noFile</c>, <c>nothing</c>,
/// <c>notSent</c>, <c>notAllowed</c>, <c>undo</c>, <c>error</c>.</param>
/// <param name="To">Who it went to, or whose chat was read (null: the owner).</param>
/// <param name="Contacts">noRecipient, contacts: the people it may write to (and the groups it may read).</param>
public sealed record TelegramAnswer(bool Ok, string? Reason = null, string? To = null, string? Undo = null, IReadOnlyList<TelegramLine>? Lines = null,
    IReadOnlyList<string>? Contacts = null, string? Message = null);

/// <summary>
/// Answers the assistants' requests with the <see cref="TelegramService"/> (in K-Desktop's process, telegram phase 4.5): who
/// may be written to is applied again here (<see cref="TelegramService.CanWriteTo"/>), whatever the model asked; in an
/// order from someone who is not the owner, only their own chat is read.
/// </summary>
public static class TelegramAssistant
{
    /// <summary>Telegram takes a photo up to 10 MB; a bigger capture goes as a file.</summary>
    private const long MaxPhoto = 10L << 20;

    private static readonly string[] OwnerWords = ["me", "owner", "yo", "mí", "mi", "dueño", "moi", "mich", "ich", "io"];

    /// <summary>
    /// With the bot and the account (phase 6): what an order from Telegram asks goes through the connection it came by
    /// (<paramref name="turnVia"/>); else the one asked for (<see cref="TelegramRequest.Via"/>), or the one
    /// <see cref="Choose"/> picks. «contacts» gives the people of both.
    /// </summary>
    public static Task<TelegramAnswer> HandleAsync(TelegramService bot, TelegramService account, TelegramRequest request, long? onlyChat = null, TelegramVia? turnVia = null)
    {
        if (request.Do == "contacts")
            return Task.FromResult(bot.On || account.On ? new TelegramAnswer(true, Contacts: Reachable(bot, account)) : new TelegramAnswer(false, "notReady"));
        if (request.Do == "undo")
            return HandleAsync(request.Undo?.StartsWith(AccountMark, StringComparison.Ordinal) == true ? account : bot, request with { Undo = request.Undo?.Replace(AccountMark, string.Empty, StringComparison.Ordinal) });
        var via = turnVia ?? request.Via?.Trim().ToLowerInvariant() switch
        {
            "bot" => TelegramVia.Bot,
            "account" or "cuenta" => TelegramVia.Account,
            _ => (TelegramVia?)null,
        };
        var telegram = via is { } chosen ? chosen == TelegramVia.Account ? account : bot : Choose(bot, account, request.To, request.Do == "read");
        return HandleAsync(telegram, request, onlyChat);
    }

    /// <summary>What marks an undo of the account's («a:chat:id»).</summary>
    private const string AccountMark = "a:";

    /// <summary>
    /// Which one writes when none is said: the account when it is on and that person (or group) is its owner or one of its
    /// contacts; else the bot; else the account. To read, the one where that chat said something last.
    /// </summary>
    public static TelegramService Choose(TelegramService bot, TelegramService account, string? to, bool read = false)
    {
        var inAccount = account.On && account.Owner is not null && ChatOf(account, to, read) is not null;
        var inBot = bot.On && bot.Owner is not null && ChatOf(bot, to, read) is not null;
        if (inAccount && inBot && read)
        {
            DateTimeOffset Last(TelegramService t) => t.Recent(ChatOf(t, to, true)!.Value, 1) is [var line] ? line.At : DateTimeOffset.MinValue;
            return Last(bot) > Last(account) ? bot : account;
        }
        return inAccount ? account : inBot ? bot : bot.On ? bot : account;
    }

    /// <summary>Who it may write to with both (each name once).</summary>
    public static IReadOnlyList<string> Reachable(TelegramService bot, TelegramService account) =>
        [.. (account.On ? Reachable(account) : []).Concat(bot.On ? Reachable(bot) : []).Distinct(StringComparer.CurrentCultureIgnoreCase)];

    /// <param name="onlyChat">An order from someone who is not the owner: the one chat it may read.</param>
    public static async Task<TelegramAnswer> HandleAsync(TelegramService telegram, TelegramRequest request, long? onlyChat = null)
    {
        if (!telegram.On || telegram.Owner is null) return new TelegramAnswer(false, "notReady");
        return request.Do switch
        {
            "send" => await SendAsync(telegram, request, onlyChat).ConfigureAwait(false),
            "read" => Read(telegram, request, onlyChat),
            "contacts" => new TelegramAnswer(true, Contacts: Reachable(telegram)),
            "undo" => await UndoAsync(telegram, request).ConfigureAwait(false),
            _ => new TelegramAnswer(false, "error", Message: "do"),
        };
    }

    /// <summary>Whether <paramref name="to"/> means «me» (empty, «me», «yo»…).</summary>
    private static bool IsOwnerWord(string? to) => OwnerWords.Contains((to ?? string.Empty).Trim().ToLowerInvariant()) || string.IsNullOrWhiteSpace(to);

    /// <summary>The chat <paramref name="to"/> means: the owner when empty («me», «yo»…), a contact, or a group by its name.</summary>
    public static long? ChatOf(TelegramService telegram, string? to, bool groups = false)
    {
        var who = (to ?? string.Empty).Trim();
        if (OwnerWords.Contains(who.ToLowerInvariant())) who = string.Empty;
        if (telegram.Recipient(who) is { } chat) return chat;
        if (!groups) return null;
        var found = telegram.Read(s => s.Groups.Where(g => !g.Ignore && string.Equals(g.Title, who, StringComparison.OrdinalIgnoreCase)).Select(g => g.Id).ToList());
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Who it may write to: the owner and the contacts with «puede escribirle».</summary>
    public static IReadOnlyList<string> Reachable(TelegramService telegram) =>
        telegram.Read(s => (IReadOnlyList<string>)[
            .. s.Owner is { } owner ? [owner.Shown + " (" + Loc.T("telegram.assistant.ownerTag") + ")"] : Array.Empty<string>(),
            .. s.Contacts.Where(c => c.MayWrite && c.Level != TelegramLevel.Ignore).Select(c => c.Shown),
        ]);

    /// <param name="requester">An order from someone who is not the owner: «me» is them (their own chat, where they wrote).</param>
    private static async Task<TelegramAnswer> SendAsync(TelegramService telegram, TelegramRequest request, long? requester = null)
    {
        long chat;
        if (requester is { } them && IsOwnerWord(request.To)) chat = them;
        else if (ChatOf(telegram, request.To) is { } found) chat = found;
        else return new TelegramAnswer(false, "noRecipient", Contacts: Reachable(telegram));
        // Whoever asked is written back where they wrote; anybody else, as the contacts say.
        if (chat != requester && !telegram.CanWriteTo(chat)) return new TelegramAnswer(false, "cantWrite", telegram.NameOf(chat));
        var text = request.Text?.Trim() ?? string.Empty;
        string? file = null;
        var kind = TelegramFileKind.Document;
        var temporary = false;
        switch (request.File?.Trim().Trim('"'))
        {
            case null or "":
                break;
            case "screenshot":
                var folder = Path.Combine(Path.GetTempPath(), "K-Desktop Telegram");
                Directory.CreateDirectory(folder);
                file = Path.Combine(folder, $"{Loc.T("telegram.file.capture")} {DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}.png");
                await File.WriteAllBytesAsync(file, Png.Encode(ScreenCapture.Capture(ScreenCapture.VirtualScreen))).ConfigureAwait(false);
                kind = new FileInfo(file).Length <= MaxPhoto ? TelegramFileKind.Photo : TelegramFileKind.Document;
                temporary = true;
                break;
            case "last":
                if (telegram.LastDownload is not { } last || !File.Exists(last)) return new TelegramAnswer(false, "noFile");
                file = last;
                break;
            case var path:
                if (!File.Exists(path)) return new TelegramAnswer(false, "noFile", Message: path);
                file = path;
                break;
        }
        if (file is not null && !temporary && IsPhoto(file) && new FileInfo(file).Length <= MaxPhoto) kind = TelegramFileKind.Photo;
        if (file is null && text.Length == 0) return new TelegramAnswer(false, "nothing");
        try
        {
            var id = await telegram.SendForIdAsync(chat, text, file, kind).ConfigureAwait(false);
            if (id is null) return new TelegramAnswer(false, "notSent", telegram.NameOf(chat), Message: telegram.Problem);
            var who = chat == telegram.Owner?.Id ? null : telegram.NameOf(chat);
            return new TelegramAnswer(true, To: who, Undo: id > 0 ? (telegram.Kind == TelegramVia.Account ? AccountMark : string.Empty) + string.Create(CultureInfo.InvariantCulture, $"{chat}:{id}") : null);
        }
        finally
        {
            if (temporary)
            {
                try { File.Delete(file!); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsPhoto(string file) => Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";

    private static TelegramAnswer Read(TelegramService telegram, TelegramRequest request, long? onlyChat)
    {
        long chat;
        if (onlyChat is { } own) chat = own;
        else if (ChatOf(telegram, request.To, groups: true) is { } found) chat = found;
        else return new TelegramAnswer(false, "noRecipient", Contacts: [.. Reachable(telegram), .. telegram.Read(s => s.Groups.Where(g => !g.Ignore).Select(g => g.Title).ToList())]);
        var name = chat == telegram.Owner?.Id ? null : telegram.GroupName(chat) ?? telegram.NameOf(chat);
        return new TelegramAnswer(true, To: name, Lines: telegram.Recent(chat, request.Max));
    }

    private static async Task<TelegramAnswer> UndoAsync(TelegramService telegram, TelegramRequest request)
    {
        var parts = (request.Undo ?? string.Empty).Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var chat) || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var message))
            return new TelegramAnswer(false, "undo");
        return await telegram.DeleteAsync(chat, message).ConfigureAwait(false) ? new TelegramAnswer(true) : new TelegramAnswer(false, "undo");
    }
}
