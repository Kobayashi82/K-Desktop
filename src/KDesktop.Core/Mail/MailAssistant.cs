using System.Text.Json;
using KDesktop.Core.Calendar;

namespace KDesktop.Core.Mail;

/// <summary>
/// What an assistant asks of the mail (mail, phase 4): <c>list</c>, <c>read</c>, <c>draft</c>, <c>send</c>, <c>move</c>,
/// <c>invitation</c> (a calendar invitation of a mail into the calendar), <c>undo</c> or <c>accounts</c>.
/// </summary>
/// <param name="Account">The account as the user calls it («trabajo», the address); empty: the first one, or all to look.</param>
/// <param name="Id">read, move, invitation, a reply: the mail (as <c>list</c> gave it).</param>
/// <param name="Op">move: <c>read</c>, <c>unread</c>, <c>archive</c>, <c>trash</c>.</param>
public sealed record MailRequest(string Do, string? Account = null, string? Id = null, bool Unread = false, bool Important = false, string? Text = null,
    string? From = null, string? Since = null, string? Until = null, int Max = 15, IReadOnlyList<string>? To = null, IReadOnlyList<string>? Cc = null,
    string? Subject = null, string? Body = null, IReadOnlyList<string>? Attachments = null, string? Op = null, string? Undo = null);

/// <summary>One mail for the model.</summary>
public sealed record MailItem(string Id, string Account, string From, string Subject, string Date, string Snippet, bool Read, bool Important,
    IReadOnlyList<string> Attachments, string? Text = null, string? ReplyTo = null);

/// <param name="Reason">When not done: <c>account</c>, <c>notFound</c>, <c>noRecipient</c>, <c>notAllowed</c>, <c>error</c>…</param>
/// <param name="NewRecipients">send, draft: the addresses the user never wrote to nor got mail from (said before sending).</param>
public sealed record MailAnswer(bool Ok, IReadOnlyList<MailItem>? Items = null, MailItem? Item = null, string? Reason = null, string? Message = null,
    string? Undo = null, IReadOnlyList<string>? Accounts = null, IReadOnlyList<string>? NewRecipients = null, int? Added = null);

/// <summary>Answers the assistants' requests with the <see cref="MailService"/> (in K-Desktop's process).</summary>
public static class MailAssistant
{
    private sealed record UndoData(string Kind, string? Draft = null, MailMoved? Moved = null, CalendarChange? Calendar = null);

    public static async Task<MailAnswer> HandleAsync(MailService mail, CalendarService? calendar, MailRequest r, CancellationToken cancel = default)
    {
        try
        {
            return r.Do switch
            {
                "accounts" => new MailAnswer(true, Accounts: [.. mail.Accounts.Select(a => $"{a.Display}{(a.Use == MailUse.SendOnly ? " (send only)" : string.Empty)}")]),
                "list" => await ListAsync(mail, r, cancel).ConfigureAwait(false),
                "read" => await ReadAsync(mail, r, cancel).ConfigureAwait(false),
                "draft" or "send" => await WriteAsync(mail, r, cancel).ConfigureAwait(false),
                "move" => await MoveAsync(mail, r, cancel).ConfigureAwait(false),
                "invitation" => await InvitationAsync(mail, calendar, r, cancel).ConfigureAwait(false),
                "undo" => await UndoAsync(mail, calendar, r, cancel).ConfigureAwait(false),
                _ => new MailAnswer(false, Reason: "op"),
            };
        }
        catch (MailException ex)
        {
            return new MailAnswer(false, Reason: "error", Message: ex.Message);
        }
        catch (Exception ex) when (ex is Accounts.AccountException or IOException or MailKit.ServiceNotConnectedException or MailKit.Net.Imap.ImapCommandException)
        {
            return new MailAnswer(false, Reason: "error", Message: ex.Message);
        }
    }

    private static MailItem Item(MailService mail, MailHeader m, string? text = null, string? replyTo = null) =>
        new(m.Id, mail.Account(m.AccountId)?.Address ?? string.Empty, m.From.ToString(), m.Subject, m.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            m.Snippet, m.Read, MailRules.IsImportant(m, mail.Settings.Important), [.. m.Attachments.Select(a => a.Name)], text, replyTo);

    private static DateTimeOffset? Day(string? text) =>
        DateTime.TryParseExact((text ?? string.Empty).Trim(), ["yyyy-M-d", "yyyy-M-d H:mm"], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? new DateTimeOffset(d) : null;

    private static MailAccountSettings? AccountOf(MailService mail, string? name, bool sending) =>
        string.IsNullOrWhiteSpace(name)
            ? mail.Accounts.FirstOrDefault(a => a.Enabled && (sending || a.Use == MailUse.All))
            : mail.FindAccount(name);

    private static async Task<MailAnswer> ListAsync(MailService mail, MailRequest r, CancellationToken cancel)
    {
        string? accountId = null;
        if (!string.IsNullOrWhiteSpace(r.Account))
        {
            if (mail.FindAccount(r.Account) is not { } account) return new MailAnswer(false, Reason: "account", Accounts: [.. mail.Accounts.Select(a => a.Display)]);
            accountId = account.Id;
        }
        var until = Day(r.Until);
        var found = await mail.ListAsync(accountId, r.Unread, r.Important, r.Text, r.From, Day(r.Since), until?.AddDays(1), Math.Clamp(r.Max, 1, 50), cancel: cancel).ConfigureAwait(false);
        return new MailAnswer(true, [.. found.Select(m => Item(mail, m))]);
    }

    private static async Task<MailAnswer> ReadAsync(MailService mail, MailRequest r, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(r.Id)) return new MailAnswer(false, Reason: "notFound");
        var content = await mail.ReadAsync(r.Id, cancel: cancel).ConfigureAwait(false);
        var replyTo = content.ReplyTo.Count > 0 ? content.ReplyTo[0].Address : content.Header.From.Address;
        return new MailAnswer(true, Item: Item(mail, content.Header, content.Text, replyTo));
    }

    private static async Task<MailAnswer> WriteAsync(MailService mail, MailRequest r, CancellationToken cancel)
    {
        var send = r.Do == "send";
        if (send && !mail.Settings.AssistantMaySend) return new MailAnswer(false, Reason: "noSending");
        var account = AccountOf(mail, r.Account, sending: true);
        if (account is null) return new MailAnswer(false, Reason: "account", Accounts: [.. mail.Accounts.Select(a => a.Display)]);
        string? inReplyTo = null;
        var to = r.To?.Where(t => t.Trim().Length > 0).ToList() ?? [];
        var subject = r.Subject ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(r.Id) && mail.Header(r.Id) is { } original)
        {
            // A reply: to whom wrote, «Re:» and in the same thread.
            inReplyTo = original.MessageId;
            if (to.Count == 0) to.Add(original.From.Address);
            if (subject.Length == 0) subject = original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? original.Subject : "Re: " + original.Subject;
            if (account.Use == MailUse.All && mail.Account(original.AccountId) is { } same) account = same;
        }
        if (to.Count == 0) return new MailAnswer(false, Reason: "noRecipient");
        var draft = new MailDraft(account.Id, to, subject, r.Body ?? string.Empty, r.Cc, r.Attachments, inReplyTo);
        var unknown = to.Concat(r.Cc ?? []).Where(a => !mail.Knows(ExtractAddress(a))).ToList();
        if (send)
        {
            await mail.SendAsync(draft, cancel).ConfigureAwait(false);
            return new MailAnswer(true, NewRecipients: unknown);
        }
        var id = await mail.SaveDraftAsync(draft, cancel).ConfigureAwait(false);
        return new MailAnswer(true, Undo: JsonSerializer.Serialize(new UndoData("draft", Draft: id), CalendarJson.Options), NewRecipients: unknown);
    }

    /// <summary>«Ana &lt;ana@x.es&gt;» → «ana@x.es».</summary>
    public static string ExtractAddress(string text)
    {
        var open = text.LastIndexOf('<');
        var close = text.LastIndexOf('>');
        return (open >= 0 && close > open ? text[(open + 1)..close] : text).Trim().ToLowerInvariant();
    }

    private static async Task<MailAnswer> MoveAsync(MailService mail, MailRequest r, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(r.Id)) return new MailAnswer(false, Reason: "notFound");
        var op = (r.Op ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delete" or "remove" or "bin" => "trash",
            var o => o,
        };
        if (op is not ("read" or "unread" or "archive" or "trash")) return new MailAnswer(false, Reason: "op");
        var header = mail.Header(r.Id);
        var moved = await mail.MoveAsync(r.Id, op, cancel).ConfigureAwait(false);
        return new MailAnswer(true, Item: header is null ? null : Item(mail, header), Undo: JsonSerializer.Serialize(new UndoData("move", Moved: moved), CalendarJson.Options));
    }

    /// <summary>The .ics of a mail (an invitation) into the default calendar.</summary>
    private static async Task<MailAnswer> InvitationAsync(MailService mail, CalendarService? calendar, MailRequest r, CancellationToken cancel)
    {
        if (calendar is null || string.IsNullOrWhiteSpace(r.Id)) return new MailAnswer(false, Reason: "notFound");
        var change = await AddInvitationAsync(mail, calendar, r.Id, cancel).ConfigureAwait(false);
        if (change is null) return new MailAnswer(false, Reason: "noInvitation");
        return new MailAnswer(true, Added: change.Added.Count, Undo: JsonSerializer.Serialize(new UndoData("calendar", Calendar: change), CalendarJson.Options));
    }

    /// <summary>
    /// Adds the citas of a mail's invitation (a .ics attached, or its text/calendar part) to the default calendar; null
    /// when it has none. Cancelled ones (METHOD:CANCEL) are not added.
    /// </summary>
    public static async Task<CalendarChange?> AddInvitationAsync(MailService mail, CalendarService calendar, string id, CancellationToken cancel = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "K-Desktop", "ics-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var files = await mail.SaveAttachmentsAsync(id, folder, "*.ics", cancel).ConfigureAwait(false);
            var added = new List<CalendarEvent>();
            foreach (var file in files)
            {
                var text = await File.ReadAllTextAsync(file, cancel).ConfigureAwait(false);
                if (text.Contains("METHOD:CANCEL", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var e in IcsFormat.Read(text).Events)
                {
                    var change = await calendar.AddAsync(e with { SourceId = string.Empty, CalendarId = string.Empty, Id = string.Empty, Etag = null }, cancel).ConfigureAwait(false);
                    added.AddRange(change.Added);
                }
            }
            return added.Count == 0 ? null : new CalendarChange { Added = added, Result = added[0] };
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<MailAnswer> UndoAsync(MailService mail, CalendarService? calendar, MailRequest r, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(r.Undo) || JsonSerializer.Deserialize<UndoData>(r.Undo, CalendarJson.Options) is not { } undo) return new MailAnswer(false, Reason: "notFound");
        var ok = undo.Kind switch
        {
            "draft" when undo.Draft is { } draft => await mail.DeleteDraftAsync(draft, cancel).ConfigureAwait(false),
            "move" when undo.Moved is { } moved => await mail.UndoMoveAsync(moved, cancel).ConfigureAwait(false),
            "calendar" when undo.Calendar is { } change && calendar is not null => await Revert(calendar, change, cancel).ConfigureAwait(false),
            _ => false,
        };
        return new MailAnswer(ok);
    }

    private static async Task<bool> Revert(CalendarService calendar, CalendarChange change, CancellationToken cancel)
    {
        await calendar.RevertAsync(change, cancel).ConfigureAwait(false);
        return true;
    }
}
