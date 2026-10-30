using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Dialogs;

/// <summary>A step: its texts are guide.{guide}.{n}.title and .text; the page to open and what to paste there.</summary>
/// <param name="Copy">Pairs of a label key (guide.copy.*) and the value copied.</param>
internal sealed record GuideStep(string? Url = null, params (string Label, string Value)[] Copy);

/// <summary>
/// The guides of what is done once outside K-Desktop, to connect accounts and mail: numbered steps with a button to open
/// each page and the values to paste, copied with one click. Texts in the language files (guide.{id}.*).
/// </summary>
public partial class GuideDialog : FluentWindow
{
    public const string Google = "google";
    public const string AppPassword = "appPassword";
    public const string Ics = "ics";
    public const string Telegram = "telegram";
    public const string TelegramAccount = "telegramAccount";

    private static readonly Dictionary<string, GuideStep[]> Guides = new(StringComparer.Ordinal)
    {
        [Google] =
        [
            new("https://console.cloud.google.com/projectcreate"),
            new("https://console.cloud.google.com/apis/library/calendar-json.googleapis.com"),
            new("https://console.cloud.google.com/auth/branding",
                ("guide.copy.home", "https://github.com/Kobayashi82/K-Desktop"),
                ("guide.copy.privacy", "https://github.com/Kobayashi82/K-Desktop#privacy"),
                ("guide.copy.domain", "github.com")),
            new("https://console.cloud.google.com/auth/audience"),
            new("https://console.cloud.google.com/auth/clients"),
            new(),
        ],
        [AppPassword] =
        [
            new("https://myaccount.google.com/apppasswords"),
            new("https://account.apple.com"),
            new("https://login.yahoo.com/account/security"),
            new(),
            new(),
        ],
        [Ics] =
        [
            new("https://calendar.google.com/calendar/r/settings"),
            new("https://outlook.live.com/calendar/0/options/calendar/SharedCalendars"),
            new(),
            new(),
        ],
        [Telegram] =
        [
            new("https://t.me/BotFather"),
            new(null, ("guide.copy.command", "/newbot")),
            new(),
            new(),
            new(),
        ],
        // The account with a number (telegram, phase 6): its own application in my.telegram.org, then logging in.
        [TelegramAccount] =
        [
            new(),
            new("https://my.telegram.org/apps",
                ("guide.copy.appTitle", "K-Desktop"),
                ("guide.copy.appShort", "kdesktop")),
            new(),
            new(),
            new(),
        ],
    };

    public GuideDialog() => InitializeComponent();

    public static void Show(Window? owner, string guide)
    {
        var dlg = new GuideDialog { Owner = owner };
        dlg.TitleBar.Title = Text($"guide.{guide}.title");
        dlg.IntroText.Text = Text($"guide.{guide}.intro");
        var steps = Guides[guide];
        for (var i = 0; i < steps.Length; i++) dlg.Steps.Children.Add(Step(guide, i + 1, steps[i], last: i == steps.Length - 1));
        dlg.ShowDialog();
    }

    /// <summary>A text of the guide, with «K-Desktop» never split at the end of a line (a non-breaking hyphen).</summary>
    private static string Text(string key) => Loc.T(key).Replace("K-Desktop", "K‑Desktop", StringComparison.Ordinal);

    private static Grid Step(string guide, int number, GuideStep step, bool last)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, last ? 0 : 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var circle = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        circle.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
        var digit = new TextBlock { Text = number.ToString(Loc.Instance.Culture), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        digit.SetResourceReference(TextBlock.ForegroundProperty, "Kd.AccentText");
        circle.Child = digit;
        grid.Children.Add(circle);

        var content = new StackPanel();
        Grid.SetColumn(content, 1);
        var title = new TextBlock { Text = Text($"guide.{guide}.{number}.title"), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        content.Children.Add(title);
        var text = new TextBlock { Text = Text($"guide.{guide}.{number}.text"), TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        content.Children.Add(text);

        foreach (var (label, value) in step.Copy) content.Children.Add(CopyRow(Loc.T(label), value));

        if (step.Url is { } url)
        {
            var open = new Button
            {
                Content = Loc.T("guide.open"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Open24 },
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = url,
            };
            AutomationProperties.SetName(open, Loc.T("guide.open") + " " + url);
            open.Click += (_, _) => ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false);
            content.Children.Add(open);
        }
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>What to paste somewhere: its label, the value and a button that copies it.</summary>
    private static FrameworkElement CopyRow(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var caption = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(caption);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var shown = new TextBlock { Text = value, FontFamily = new FontFamily("Consolas"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        shown.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        box.Child = shown;
        row.Children.Add(box);

        var copy = new Button { Icon = new SymbolIcon { Symbol = SymbolRegular.Copy24 }, ToolTip = Loc.T("guide.copy"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(copy, Loc.T("guide.copy") + " " + label);
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(value);
                App.Current.Ui.ShowOsd(Loc.T("guide.copied", value), Core.Engine.OsdKind.Success);
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        };
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        panel.Children.Add(row);
        return panel;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
