using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → General (stage 1.8): turn the module on and see how it is doing. Who is on the desktop is
/// chosen in Options → Assistants (several at once since stage 1.11). The word «for the assistant» (stage 5.1) calls
/// whoever answers what is not addressed to anyone, or the module without a body.
/// </summary>
public partial class AssistantGeneralSection : UserControl
{
    private readonly App _app = App.Current;
    private bool _filling;

    public AssistantGeneralSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => _app.AssistantChanged -= Refresh;
    }

    private void Refresh()
    {
        var (text, brush) = AssistantLook.Summary(_app);
        StateText.Text = text;
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        PanelButton.Visibility = _app.Assistant is null ? Visibility.Collapsed : Visibility.Visible;
        FillAnyWake();
        ShowTalkKey();
        FillManoeuvres();
    }

    /// <summary>An option of a combo with its value (manoeuvres, long commands).</summary>
    private sealed record Choice<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>The choices for «more than N actions: only the window ones».</summary>
    private static readonly int[] AnimateLimits = [3, 5, 8];

    /// <summary>The speed of the manoeuvres and the rule for long commands (stage 6.7).</summary>
    private void FillManoeuvres()
    {
        _filling = true;
        var s = _app.Config.Settings;
        List<Choice<string>> speeds =
        [
            new("showy", Loc.T("assistantOptions.manoeuvresShowy")),
            new("normal", Loc.T("assistantOptions.manoeuvresNormal")),
            new("fast", Loc.T("assistantOptions.manoeuvresFast")),
        ];
        ManoeuvresBox.ItemsSource = speeds;
        ManoeuvresBox.SelectedItem = speeds.FirstOrDefault(o => o.Value == s.AssistantManoeuvres) ?? speeds[1];

        List<Choice<string>> asks =
        [
            new(AskPolicy.Never, Loc.T("assistantOptions.askNever")),
            new(AskPolicy.Risky, Loc.T("assistantOptions.askRisky")),
            new(AskPolicy.First, Loc.T("assistantOptions.askFirst")),
        ];
        AskBeforeBox.ItemsSource = asks;
        AskBeforeBox.SelectedItem = asks.FirstOrDefault(o => o.Value == s.AssistantAskBefore) ?? asks[1];

        List<Choice<int>> limits = [new(0, Loc.T("assistantOptions.animateAll")), .. AnimateLimits.Select(n => new Choice<int>(n, Loc.T("assistantOptions.animateMore", n)))];
        if (limits.All(o => o.Value != s.AssistantAnimateMax)) limits.Add(new(s.AssistantAnimateMax, Loc.T("assistantOptions.animateMore", s.AssistantAnimateMax)));
        AnimateBox.ItemsSource = limits;
        AnimateBox.SelectedItem = limits.First(o => o.Value == s.AssistantAnimateMax);

        List<Choice<string>> languages = [new(string.Empty, Loc.T("assistantOptions.languageSame")), .. Loc.Available().Select(l => new Choice<string>(l.Code, l.Name))];
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = languages.FirstOrDefault(o => o.Value == s.AssistantLanguage) ?? languages[0];
        _filling = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        // A new language: the assistant process starts again with it.
        if (!_filling && LanguageBox.SelectedItem is Choice<string> choice) _app.Config.Settings.AssistantLanguage = choice.Value;
    }

    private void OnManoeuvresChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && ManoeuvresBox.SelectedItem is Choice<string> choice) _app.Config.Settings.AssistantManoeuvres = choice.Value;
    }

    private void OnAskBeforeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && AskBeforeBox.SelectedItem is Choice<string> choice) _app.Config.Settings.AssistantAskBefore = choice.Value;
    }

    private void OnAnimateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && AnimateBox.SelectedItem is Choice<int> choice) _app.Config.Settings.AssistantAnimateMax = choice.Value;
    }

    private void ShowTalkKey()
    {
        var hotkey = _app.Config.Settings.AssistantTalkHotkey;
        var none = string.IsNullOrWhiteSpace(hotkey);
        TalkKeyButton.Content = none ? Loc.T("settings.voiceNoHotkey") : hotkey;
        TalkKeyClear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCaptureTalkKey(object sender, RoutedEventArgs e)
    {
        TalkKeyButton.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => TalkKeyButton.Content = preview + "…",
            hotkey =>
            {
                if (hotkey is not null) _app.Config.Settings.AssistantTalkHotkey = Core.Input.Hotkey.Normalize(hotkey);
                ShowTalkKey();
            },
            Window.GetWindow(this));
    }

    private void OnClearTalkKey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.AssistantTalkHotkey = string.Empty;
        ShowTalkKey();
    }

    /// <summary>The recorded words; one of an assistant says whose it is (choosing it here takes it from that one).</summary>
    private void FillAnyWake()
    {
        _filling = true;
        var owners = _app.Config.Settings.AssistantWakeWords;
        List<WakeOption> options = [new(null, Loc.T("assistantOptions.wakeNone"))];
        foreach (var word in _app.Voice.WakeWords.Words)
        {
            var owner = owners.FirstOrDefault(p => p.Value == word.Id).Key;
            options.Add(new(word.Id, owner is null or AssistantTargeting.AnyAssistant ? word.Name : Loc.T("assistantOptions.wakeOf", word.Name, App.AssistantName(owner))));
        }
        AnyWakeBox.ItemsSource = options;
        AnyWakeBox.SelectedItem = options.FirstOrDefault(o => o.Id == _app.AnyAssistantWakeWord) ?? options[0];
        _filling = false;
    }

    private void OnAnyWakeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || AnyWakeBox.SelectedItem is not WakeOption option || option.Id == _app.AnyAssistantWakeWord) return;
        // After this event: the box is filled again when it changes.
        Dispatcher.BeginInvoke(() => _app.SetAssistantWakeWord(AssistantTargeting.AnyAssistant, option.Id));
    }

    private void OnRecordAnyWake(object sender, RoutedEventArgs e)
    {
        if (!VoiceModels.AllInstalled || _app.Voice.Models.IsDownloading)
        {
            _app.Ui.ShowOsd(Loc.T("wake.needModels"), OsdKind.Info);
            return;
        }
        if (Dialogs.WakeWordDialog.Record(Window.GetWindow(this), suggestedName: Loc.T("assistantOptions.anyWakeName")) is { } word)
            _app.SetAssistantWakeWord(AssistantTargeting.AnyAssistant, word.Id);
    }

    private void OnOpenPanel(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("assistant");
}
