using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Model;

/// <summary>What kind of message «Llega un mensaje de Telegram» waits for.</summary>
public enum TelegramMessageKind { Any, Text, Voice, File }

/// <summary>
/// «Llega un mensaje de Telegram» (telegram, phase 3.2): a message of someone it serves (you, a contact, or anybody if
/// «todos los demás» may talk), from someone, with some words or of a kind. Its file is already in Downloads. The commands
/// get it in {telegram_de}, {telegram_texto}, {telegram_archivo} and {telegram_id} (and {telegram_from}, {telegram_text},
/// {telegram_file}); {resultado} is the text.
/// </summary>
public sealed partial class TelegramTrigger : EventTrigger
{
    /// <summary>Only the owner's messages.</summary>
    [ObservableProperty] private bool _onlyOwner = true;

    /// <summary>Part of the name or the @user of who writes; empty: anyone.</summary>
    [ObservableProperty] private string _from = string.Empty;

    /// <summary>Words the text has (all of them); empty: any.</summary>
    [ObservableProperty] private string _contains = string.Empty;

    [ObservableProperty] private TelegramMessageKind _messageKind = TelegramMessageKind.Any;

    public override EventKind Kind => EventKind.Telegram;

    public override string Summary
    {
        get
        {
            var who = OnlyOwner ? Loc.T("event.summary.telegram.owner") : From.Trim().Length > 0 ? Loc.T("event.summary.telegram.from", From.Trim()) : Loc.T("event.summary.telegram.any");
            if (MessageKind != TelegramMessageKind.Any) who += " · " + Loc.T("event.telegramKind." + MessageKind);
            return Contains.Trim().Length > 0 ? Loc.T("event.summary.telegram.contains", who, Contains.Trim()) : who;
        }
    }

    /// <summary>Whether a message that arrived is one this event waits for.</summary>
    public bool Matches(TelegramIncoming m, bool fromOwner)
    {
        if (OnlyOwner && !fromOwner) return false;
        static string Fold(string s) => Mail.MailRules.Fold(s);
        if (!OnlyOwner && From.Trim().Length > 0 && !Fold(m.From.Name + " @" + m.From.Username).Contains(Fold(From).TrimStart('@'), StringComparison.Ordinal)) return false;
        var ok = MessageKind switch
        {
            TelegramMessageKind.Text => m.Content == TelegramContent.Text,
            TelegramMessageKind.Voice => m.Content == TelegramContent.Voice,
            TelegramMessageKind.File => m.Content is TelegramContent.Photo or TelegramContent.Document,
            _ => true,
        };
        if (!ok) return false;
        return Contains.Trim().Length == 0 || Fold(Contains).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Fold(m.Text).Contains(w, StringComparison.Ordinal));
    }
}
