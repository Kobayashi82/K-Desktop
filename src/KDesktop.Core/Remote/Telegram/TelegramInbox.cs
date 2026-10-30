namespace KDesktop.Core.Remote.Telegram;

/// <summary>Where a message goes: dropped, the owner's, a contact's (with its level), someone new's, or held by a limit.</summary>
public enum TelegramRoute { Drop, Owner, Contact, Stranger, Limited }

/// <summary>What was decided about a message, with the level it is served with and why (for the log and the tests).</summary>
public sealed record TelegramVerdict(TelegramRoute Route, TelegramLevel Level, string Why)
{
    public bool Served => Route is TelegramRoute.Owner or TelegramRoute.Contact or TelegramRoute.Stranger && Level != TelegramLevel.Ignore;
}

/// <summary>
/// Sorts what arrives (telegram, phase 2), without remembering anything: channels never; in a group, only what mentions
/// the bot or replies to it, and never in an ignored group; the owner always; the blocked never; a contact with its level;
/// anybody else with the level of «todos los demás» (never above «Solo charla» without «Entiendo el riesgo»). It is the
/// level of whoever writes that counts, also in groups.
/// </summary>
public static class TelegramInbox
{
    public static TelegramVerdict Classify(TelegramIncoming m, TelegramSettings s, long botId, string botUsername)
    {
        if (m.ChatKind == TelegramChatKind.Channel) return new(TelegramRoute.Drop, TelegramLevel.Ignore, "channel");
        if (m.ChatKind == TelegramChatKind.Group && !m.IsButton)
        {
            if (s.Groups.Any(g => g.Id == m.ChatId && g.Ignore)) return new(TelegramRoute.Drop, TelegramLevel.Ignore, "groupIgnored");
            if (!Addressed(m, botId, botUsername)) return new(TelegramRoute.Drop, TelegramLevel.Ignore, "notAddressed");
        }
        if (s.Owner?.Id == m.From.Id) return new(TelegramRoute.Owner, TelegramLevel.LikeOwner, "owner");
        if (s.Blocked.Any(b => b.Id == m.From.Id)) return new(TelegramRoute.Drop, TelegramLevel.Ignore, "blocked");
        if (s.Contacts.FirstOrDefault(c => c.Id == m.From.Id) is { } contact)
            return contact.Level == TelegramLevel.Ignore ? new(TelegramRoute.Drop, TelegramLevel.Ignore, "ignored") : new(TelegramRoute.Contact, contact.Level, "contact");
        return new(TelegramRoute.Stranger, s.Others.Effective, "stranger");
    }

    /// <summary>In a group: «@bot» in the text («/comandos@bot» too) or a reply to one of the bot's messages.</summary>
    public static bool Addressed(TelegramIncoming m, long botId, string botUsername) =>
        m.ReplyToUserId == botId
        || (botUsername.Length > 0 && m.Text.Contains("@" + botUsername, StringComparison.OrdinalIgnoreCase));

    /// <summary>The text without the mention of the bot («@kdesktop_bot pon música» → «pon música»).</summary>
    public static string WithoutMention(string text, string botUsername) =>
        botUsername.Length == 0 ? text.Trim() : text.Replace("@" + botUsername, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
}

/// <summary>
/// The limits that need memory (telegram, phase 2): messages a minute from each contact, and messages an hour to contacts
/// who never wrote. Thread-safe.
/// </summary>
public sealed class TelegramLimiter
{
    private readonly Dictionary<long, Queue<DateTimeOffset>> _in = [];
    private readonly Queue<DateTimeOffset> _outNew = new();
    private readonly Dictionary<long, DateTimeOffset> _told = [];
    private readonly Lock _gate = new();

    /// <summary>Counts a message from <paramref name="id"/>; false when it is over <paramref name="perMinute"/> in the last minute.</summary>
    public bool AllowIn(long id, DateTimeOffset now, int perMinute)
    {
        lock (_gate)
        {
            if (!_in.TryGetValue(id, out var times)) _in[id] = times = new Queue<DateTimeOffset>();
            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1)) times.Dequeue();
            if (times.Count >= Math.Max(1, perMinute)) return false;
            times.Enqueue(now);
            return true;
        }
    }

    /// <summary>Counts a message to someone who never wrote; false when it is over <paramref name="perHour"/> in the last hour.</summary>
    public bool AllowNew(DateTimeOffset now, int perHour)
    {
        lock (_gate)
        {
            while (_outNew.Count > 0 && now - _outNew.Peek() >= TimeSpan.FromHours(1)) _outNew.Dequeue();
            if (_outNew.Count >= Math.Max(0, perHour)) return false;
            _outNew.Enqueue(now);
            return true;
        }
    }

    /// <summary>Whether to tell <paramref name="id"/> «ahora no puedo» (once every <paramref name="every"/>, not each message).</summary>
    public bool ShouldTell(long id, DateTimeOffset now, TimeSpan every)
    {
        lock (_gate)
        {
            if (_told.TryGetValue(id, out var last) && now - last < every) return false;
            _told[id] = now;
            return true;
        }
    }
}
