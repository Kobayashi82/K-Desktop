using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>What «Enviar por Telegram» sends.</summary>
public enum TelegramSendWhat { Text, File, Screenshot, Clipboard }

/// <summary>
/// «Enviar por Telegram» (telegram, phase 3.1): to the owner or a contact (one K-Desktop may write to), a text with
/// variables, a file, a screenshot or what is in the clipboard (files, an image or text); the text goes with it.
/// </summary>
public partial class TelegramSendAction : CommandAction
{
    /// <summary>Empty: the owner; else a contact's id (or its name or @user; variables allowed).</summary>
    [ObservableProperty] private string _to = string.Empty;

    [ObservableProperty] private TelegramSendWhat _what = TelegramSendWhat.Text;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _file = string.Empty;
    [ObservableProperty] private bool _showOsd = true;

    /// <summary>A contact's name by its id, for the summary (set by K-Desktop).</summary>
    public static Func<string, string?>? NameOf { get; set; }

    [JsonIgnore] public override bool GivesResult => true;

    public override ActionKind Kind => ActionKind.TelegramSend;

    public override string Summary
    {
        get
        {
            var who = To.Trim().Length == 0 ? Loc.T("summary.telegram.owner") : NameOf?.Invoke(To.Trim()) ?? To.Trim();
            var what = What switch
            {
                TelegramSendWhat.Text => Text.Trim().Length == 0 ? "…" : $"«{(Text.Trim().Length > 40 ? Text.Trim()[..40] + "…" : Text.Trim())}»",
                TelegramSendWhat.File => File.Trim().Length == 0 ? "…" : System.IO.Path.GetFileName(File.Trim().Trim('"')),
                _ => Loc.T("summary.telegram." + What),
            };
            return Loc.T("summary.telegramSend", who, what);
        }
    }
}
