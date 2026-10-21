using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Voice (stage 5.4): who says the answers aloud (Kokoro, downloaded here for the interface
/// language, with espeak-ng apart when the language needs it; Windows' voices; beeps; nobody), which voice or mix of two,
/// how fast and how loud, and «Probar» (stage 5.6: by whom, in which mood, and stopping it); and when they speak aloud.
/// What really speaks comes from the assistant process (<c>voiceState</c>).
/// </summary>
public partial class AssistantVoiceSection : UserControl
{
    private sealed record Option(string Value, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>The moods «Probar» offers (empty: plain), the ones the assistants set.</summary>
    private static readonly string[] TestMoods = ["", "happy", "error"];

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
        var language = _app.KokoroLanguage;

        List<Option> engines =
        [
            new("auto", Loc.T("assistantVoice.engineAuto")),
            new("kokoro", Loc.T("assistantVoice.engineKokoro")),
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
            "kokoro" => Loc.T("assistantVoice.stateKokoro", VoiceTitle(_app.KokoroVoice)),
            "windows" => Loc.T("assistantVoice.stateWindows", s.AssistantVoiceWindows is { Length: > 0 } w && state.WindowsVoices.Contains(w) ? w : state.WindowsVoices.Count > 0 ? state.WindowsVoices[0] : "?"),
            "beeps" => Loc.T("assistantVoice.stateBeeps"),
            _ => Loc.T("assistantVoice.stateOff"),
        };

        // Kokoro: downloaded, downloading or still to download (German has no Kokoro voice).
        var download = _app.KokoroDownload;
        var voice = _app.KokoroVoice;
        var languageName = CultureInfo.GetCultureInfo(language).NativeName;
        var supported = KokoroModels.Supports(language);
        var missing = supported ? KokoroModels.Missing(language, voice) : 0;
        var size = KokoroModels.InstalledSize();
        KokoroText.Text = !supported ? Loc.T("assistantVoice.kokoroNoLanguage", languageName)
            : download.IsDownloading ? Loc.T("assistantVoice.downloading", Mb(download.Done), Mb(download.Total))
            : missing == 0 ? Loc.T("assistantVoice.kokoroReady", languageName, Mb(size))
            : Loc.T("assistantVoice.kokoroMissing", languageName, Mb(missing));
        EspeakText.Text = supported && KokoroModels.NeedsEspeak(language) ? Loc.T("assistantVoice.espeakNote", languageName) : string.Empty;
        EspeakText.Visibility = EspeakText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Content = Loc.T("assistantVoice.download", Mb(missing));
        DownloadButton.Visibility = supported && !download.IsDownloading && missing > 0 ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = !download.IsDownloading && size > 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Margin = new Thickness(DownloadButton.Visibility == Visibility.Visible ? 8 : 0, 0, 0, 0);
        DownloadBar.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        DownloadBar.Value = download.Total > 0 ? download.Done / (double)download.Total : 0;

        // The voice, and another one to mix it with.
        KokoroVoices.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        var installed = KokoroModels.InstalledVoices(language);
        var parts = KokoroModels.Parts(voice);
        var first = parts.Count > 0 ? parts[0].Name : KokoroModels.DefaultVoice(language);
        var second = parts.Count > 1 ? parts[1].Name : null;
        Option Named(string v) => new(v, installed.Contains(v) ? VoiceTitle(v) : Loc.T("assistantVoice.voiceMissing", VoiceTitle(v)));
        var voices = KokoroModels.Voices(language).Select(Named).ToList();
        KokoroVoiceBox.ItemsSource = voices;
        KokoroVoiceBox.SelectedItem = voices.FirstOrDefault(v => v.Value == first);
        List<Option> mixes = [new(string.Empty, Loc.T("assistantVoice.mixNone")), .. KokoroModels.Voices(language).Where(v => v != first).Select(Named)];
        MixVoiceBox.ItemsSource = mixes;
        MixVoiceBox.SelectedItem = mixes.FirstOrDefault(v => v.Value == (second ?? string.Empty)) ?? mixes[0];
        MixRow.Visibility = second is null ? Visibility.Collapsed : Visibility.Visible;
        if (second is not null)
        {
            var share = parts[1].Weight / parts.Sum(p => p.Weight);
            MixSlider.Value = Math.Clamp(Math.Round(share * 20) * 5, 10, 90);
            MixLabel.Text = Loc.T("assistantVoice.mixShareOf", KokoroModels.DisplayName(second), KokoroModels.DisplayName(first));
            MixValue.Text = $"{MixSlider.Value:0} %";
        }

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
        // Empty or still the sentence it had (another language, another assistant): the one for now.
        if (TestBox.Text.Length == 0 || TestBox.Text == _testDefault)
            TestBox.Text = _testDefault = App.TestSentence(_app.LanguageOfAssistant(picked), picked is { } name ? App.AssistantName(name) : "K-Desktop");
        _filling = false;
    }

    /// <summary>The test sentence last put in the box (it is replaced while the user has not written another).</summary>
    private string? _testDefault;

    /// <summary>«ef_dora» → «Dora (mujer)»; a mix → «Dora + Alex» (for English, also British or American).</summary>
    internal static string VoiceTitle(string spec, string? language = null)
    {
        var names = KokoroModels.Names(spec);
        if (names.Count != 1) return string.Join(" + ", names.Select(KokoroModels.DisplayName));
        var voice = names[0];
        var who = Loc.T(KokoroModels.IsFemale(voice) ? "assistantVoice.female" : "assistantVoice.male");
        if ((language ?? App.Current.KokoroLanguage) == "en") who += ", " + Loc.T(KokoroModels.IsBritish(voice) ? "assistantVoice.british" : "assistantVoice.american");
        return $"{KokoroModels.DisplayName(voice)} ({who})";
    }

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

    /// <summary>The voice or the one it is mixed with changed: the mix keeps its share (half when it is new).</summary>
    private void OnKokoroVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || KokoroVoiceBox.SelectedItem is not Option voice) return;
        var with = MixVoiceBox.SelectedItem is Option { Value.Length: > 0 } mix && mix.Value != voice.Value ? mix.Value : null;
        var share = MixRow.Visibility == Visibility.Visible ? MixSlider.Value / 100 : 0.5;
        Choose(KokoroModels.Mix(voice.Value, with, share));
    }

    private void OnMixChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || KokoroVoiceBox.SelectedItem is not Option voice || MixVoiceBox.SelectedItem is not Option { Value.Length: > 0 } mix) return;
        MixValue.Text = $"{MixSlider.Value:0} %";
        Choose(KokoroModels.Mix(voice.Value, mix.Value, MixSlider.Value / 100));
    }

    private void Choose(string spec)
    {
        if (spec == _app.KokoroVoice) return;
        // After this event: the lists are made again.
        Dispatcher.BeginInvoke(() =>
        {
            _app.ChooseKokoroVoice(spec);
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
        _app.KokoroDownload.Start();
        Refresh();
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e) => _app.KokoroDownload.Cancel();

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        DeleteButton.IsEnabled = false;
        try { await _app.DeleteKokoroAsync(); }
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
