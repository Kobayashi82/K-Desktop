using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Voice (stage 5.4): who says the answers aloud (Pocket TTS, downloaded here for the interface
/// language; Windows' voices; beeps; nobody), which voice, how fast and how loud, and «Probar» (stage 5.6: by whom, in
/// which mood, and stopping it); and when they speak aloud. What really speaks comes from the assistant process
/// (<c>voiceState</c>).
/// </summary>
public partial class AssistantVoiceSection : UserControl
{
    private sealed record Option(string Value, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>The moods «Probar» offers (empty: plain), the ones every profile has.</summary>
    private static readonly string[] TestMoods = ["", "happy", "error", "far"];

    private readonly App _app = App.Current;
    private bool _filling;

    public AssistantVoiceSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantVoiceChanged += Refresh;
            _app.AssistantChanged += Refresh;
            _app.AskAssistantVoice("state");
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantVoiceChanged -= Refresh;
            _app.AssistantChanged -= Refresh;
        };
    }

    private void Refresh()
    {
        _filling = true;
        var s = _app.Config.Settings;
        var language = App.PocketLanguage;

        List<Option> engines =
        [
            new("auto", Loc.T("assistantVoice.engineAuto")),
            new("pocket", Loc.T("assistantVoice.enginePocket")),
            new("windows", Loc.T("assistantVoice.engineWindows")),
            new("beeps", Loc.T("assistantVoice.engineBeeps")),
            new("off", Loc.T("assistantVoice.engineOff")),
        ];
        EngineBox.ItemsSource = engines;
        EngineBox.SelectedItem = engines.FirstOrDefault(e => e.Value == s.AssistantVoiceEngine) ?? engines[0];

        List<Option> profiles = [new(string.Empty, Loc.T("assistantVoice.profileOwn")), .. VoiceProfile.BuiltIn.Select(p => new Option(p.Id, Loc.T("assistantVoice.profile." + p.Id)))];
        ProfileBox.ItemsSource = profiles;
        ProfileBox.SelectedItem = profiles.FirstOrDefault(p => p.Value == s.AssistantVoiceProfile) ?? profiles[0];

        StateText.Text = _app.AssistantVoice is not { } state ? Loc.T("assistantVoice.stateUnknown") : state.Engine switch
        {
            "pocket" => Loc.T("assistantVoice.statePocket", Title(_app.PocketVoice)),
            "windows" => Loc.T("assistantVoice.stateWindows", s.AssistantVoiceWindows is { Length: > 0 } w && state.WindowsVoices.Contains(w) ? w : state.WindowsVoices.Count > 0 ? state.WindowsVoices[0] : "?"),
            "beeps" => Loc.T("assistantVoice.stateBeeps"),
            _ => Loc.T("assistantVoice.stateOff"),
        };

        // Pocket TTS: downloaded, downloading or still to download.
        var download = _app.PocketDownload;
        var voice = _app.PocketVoice;
        var missing = PocketModels.Missing(language, voice);
        var languageName = CultureInfo.GetCultureInfo(language).NativeName;
        PocketText.Text = download.IsDownloading
            ? Loc.T("assistantVoice.downloading", Mb(download.Done), Mb(download.Total))
            : missing == 0 ? Loc.T("assistantVoice.pocketReady", languageName, Mb(PocketModels.InstalledSize()))
            : Loc.T("assistantVoice.pocketMissing", languageName, Mb(missing));
        DownloadButton.Content = Loc.T("assistantVoice.download", Mb(missing));
        DownloadButton.Visibility = !download.IsDownloading && missing > 0 ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = !download.IsDownloading && PocketModels.InstalledSize() > 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Margin = new Thickness(DownloadButton.Visibility == Visibility.Visible ? 8 : 0, 0, 0, 0);
        DownloadBar.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        DownloadBar.Value = download.Total > 0 ? download.Done / (double)download.Total : 0;

        var installed = PocketModels.InstalledVoices(language);
        var voices = PocketModels.Voices(language)
            .Select(v => new Option(v, installed.Contains(v) ? Title(v) : Loc.T("assistantVoice.voiceMissing", Title(v)))).ToList();
        PocketVoiceBox.ItemsSource = voices;
        PocketVoiceBox.SelectedItem = voices.FirstOrDefault(v => v.Value == voice);

        List<Option> windows = [new(string.Empty, Loc.T("assistantVoice.windowsFirst")), .. (_app.AssistantVoice?.WindowsVoices ?? []).Select(v => new Option(v, v))];
        WindowsVoiceBox.ItemsSource = windows;
        WindowsVoiceBox.SelectedItem = windows.FirstOrDefault(w => w.Value == s.AssistantVoiceWindows) ?? windows[0];
        WindowsHint.Text = _app.AssistantVoice is { WindowsVoices.Count: 0 } ? Loc.T("assistantVoice.windowsNone", languageName) : Loc.T("assistantVoice.windowsVoiceHint");

        // «Probar»: who says it (the assistants on the desktop, or only the voice) and in which mood.
        var running = _app.Assistant is { IsRunning: true };
        TestButton.IsEnabled = StopButton.IsEnabled = running;
        var picked = _app.PickAssistant();
        List<Option> who = [.. _app.AssistantsOnDesktop.Select(id => new Option(id, App.AssistantName(id))), new(string.Empty, Loc.T("assistantVoice.testVoiceOnly"))];
        var chosen = (WhoBox.SelectedItem as Option)?.Value ?? picked ?? string.Empty;
        WhoBox.ItemsSource = who;
        WhoBox.SelectedItem = who.FirstOrDefault(o => o.Value == chosen) ?? who[0];
        if (MoodBox.ItemsSource is null)
        {
            List<Option> moods = [.. TestMoods.Select(m => new Option(m, Loc.T("assistantVoice.mood." + (m.Length > 0 ? m : "plain"))))];
            MoodBox.ItemsSource = moods;
            MoodBox.SelectedItem = moods[0];
        }
        if (TestBox.Text.Length == 0)
            TestBox.Text = Loc.T("assistantVoice.testText", picked is { } name ? App.AssistantName(name) : "K-Desktop");
        _filling = false;
    }

    /// <summary>«eve» → «Eve», «bill_boerst» → «Bill Boerst».</summary>
    private static string Title(string voice) =>
        string.Join(' ', voice.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0", Loc.Instance.Culture);

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || EngineBox.SelectedItem is not Option option) return;
        _app.Config.Settings.AssistantVoiceEngine = option.Value;
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ProfileBox.SelectedItem is not Option option) return;
        _app.Config.Settings.AssistantVoiceProfile = option.Value;
    }

    private void OnPocketVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || PocketVoiceBox.SelectedItem is not Option option || option.Value == _app.PocketVoice) return;
        // After this event: the lists are made again.
        Dispatcher.BeginInvoke(() =>
        {
            _app.ChoosePocketVoice(option.Value);
            Refresh();
        });
    }

    private void OnWindowsVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || WindowsVoiceBox.SelectedItem is not Option option) return;
        _app.Config.Settings.AssistantVoiceWindows = option.Value;
    }

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        _app.PocketDownload.Start();
        Refresh();
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e) => _app.PocketDownload.Cancel();

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        DeleteButton.IsEnabled = false;
        try { await _app.DeletePocketAsync(); }
        finally { DeleteButton.IsEnabled = true; }
    }

    private void OnTest(object sender, RoutedEventArgs e) => Test();

    private void OnStopTest(object sender, RoutedEventArgs e) => _app.StopAssistantVoiceTest();

    private void OnTestKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Test();
    }

    private void Test() => _app.TestAssistantVoice(TestBox.Text,
        MoodBox.SelectedItem is Option { Value.Length: > 0 } mood ? mood.Value : null,
        (WhoBox.SelectedItem as Option)?.Value ?? string.Empty);
}
