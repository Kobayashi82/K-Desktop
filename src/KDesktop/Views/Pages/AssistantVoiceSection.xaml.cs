using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Voice (stage 5.4): who says the answers aloud (Kokoro, downloaded here for the interface
/// language, with espeak-ng apart when the language needs it; Piper, a model per voice, since 5.0.110; Windows' voices;
/// beeps; nobody), which voice or mix of two,
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
            new("piper", Loc.T("assistantVoice.enginePiper")),
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
            "piper" => Loc.T("assistantVoice.statePiper", PiperTitle(_app.PiperVoice)),
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

        RefreshPiper(language);

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

    /// <summary>Piper: what is on disk and what is missing, its voices of the language and, for one of several, who speaks.</summary>
    private void RefreshPiper(string language)
    {
        var download = _app.PiperDownload;
        var spec = _app.PiperVoice;
        var missing = _app.PiperMissing ? PiperModels.Missing(spec) : 0;
        var size = PiperModels.InstalledSize();
        var unused = PiperModels.AllInstalled.Except(_app.PiperVoicesInUse()).Count();
        PiperText.Text = download.IsDownloading ? Loc.T("assistantVoice.downloading", Mb(download.Done), Mb(download.Total))
            : missing == 0 ? Loc.T("assistantVoice.piperReady", PiperTitle(spec), Mb(size))
            : Loc.T("assistantVoice.piperMissing", PiperTitle(spec), Mb(missing));
        PiperDownloadButton.Content = Loc.T("assistantVoice.download", Mb(missing));
        PiperDownloadButton.Visibility = !download.IsDownloading && missing > 0 ? Visibility.Visible : Visibility.Collapsed;
        PiperCancelButton.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        PiperDeleteButton.Visibility = !download.IsDownloading && size > 0 ? Visibility.Visible : Visibility.Collapsed;
        PiperDeleteButton.Margin = new Thickness(PiperDownloadButton.Visibility == Visibility.Visible ? 8 : 0, 0, 0, 0);
        PiperDeleteOthersButton.Content = Loc.T("assistantVoice.deleteUnused", unused);
        PiperDeleteOthersButton.Visibility = !download.IsDownloading && unused > 0 ? Visibility.Visible : Visibility.Collapsed;
        PiperBar.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        PiperBar.Value = download.Total > 0 ? download.Done / (double)download.Total : 0;

        var voices = PiperModels.Voices(language).Select(v => new Option(v.Key, PiperOption(v))).ToList();
        PiperVoiceBox.ItemsSource = voices;
        PiperVoiceBox.SelectedItem = voices.FirstOrDefault(v => v.Value == PiperModels.KeyOf(spec));
        var speakers = PiperModels.Find(spec)?.Speakers ?? [];
        PiperSpeakerRow.Visibility = speakers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        List<Option> who = [.. speakers.Select(name => new Option(name, SpeakerName(name, alone: true)))];
        PiperSpeakerBox.ItemsSource = who;
        PiperSpeakerBox.SelectedItem = who.FirstOrDefault(o => o.Value == PiperModels.SpeakerOf(spec)) ?? who.FirstOrDefault();
    }

    /// <summary>«Davefx (España, calidad media)», and «sin descargar» when it is not on disk.</summary>
    internal static string PiperOption(PiperVoice voice)
    {
        var title = Loc.T("assistantVoice.piperTitle", voice.Name, Loc.T("assistantVoice.region." + voice.Region), Loc.T("assistantVoice.quality." + voice.Quality),
            Mb(voice.Size));
        return PiperModels.IsDownloaded(voice.Key) ? title : Loc.T("assistantVoice.voiceMissing", title);
    }

    /// <summary>«Sharvard (mujer)» for a voice and one of its speakers; «Davefx» for a voice of one.</summary>
    internal static string PiperTitle(string spec)
    {
        if (PiperModels.Find(spec) is not { } voice) return spec;
        return PiperModels.SpeakerOf(spec) is { } speaker ? $"{voice.Name} ({SpeakerName(speaker)})" : voice.Name;
    }

    /// <summary>
    /// A speaker of a Piper voice: «M»/«F» as man/woman (as in «Dora (mujer)»; with <paramref name="alone"/>, «Mujer»), the
    /// rest as they come, capitalised («Angry», «Jessica»).
    /// </summary>
    internal static string SpeakerName(string speaker, bool alone = false)
    {
        var name = speaker switch
        {
            "M" => Loc.T("assistantVoice.male"),
            "F" => Loc.T("assistantVoice.female"),
            _ => speaker,
        };
        return alone || name == speaker ? char.ToUpper(name[0], Loc.Instance.Culture) + name[1..] : name;
    }

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

    /// <summary>Another Piper voice, or another speaker of it (a new voice starts with its first speaker).</summary>
    private void OnPiperVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || PiperVoiceBox.SelectedItem is not Option voice) return;
        var speakers = PiperModels.Find(voice.Value)?.Speakers ?? [];
        var speaker = ReferenceEquals(sender, PiperSpeakerBox) && PiperSpeakerBox.SelectedItem is Option chosen ? chosen.Value
            : speakers.Count > 0 && PiperModels.KeyOf(_app.PiperVoice) == voice.Value ? PiperModels.SpeakerOf(_app.PiperVoice) : null;
        var spec = PiperModels.Spec(voice.Value, speaker);
        if (spec == _app.PiperVoice) return;
        Dispatcher.BeginInvoke(() =>
        {
            _app.ChoosePiperVoice(spec);
            Refresh();
        });
    }

    private void OnPiperDownload(object sender, RoutedEventArgs e)
    {
        _app.PiperDownload.Start();
        Refresh();
    }

    private void OnPiperCancel(object sender, RoutedEventArgs e) => _app.PiperDownload.Cancel();

    private async void OnPiperDelete(object sender, RoutedEventArgs e) => await DeletePiper(PiperDeleteButton, onlyUnused: false);

    private async void OnPiperDeleteOthers(object sender, RoutedEventArgs e) => await DeletePiper(PiperDeleteOthersButton, onlyUnused: true);

    private async Task DeletePiper(Button button, bool onlyUnused)
    {
        button.IsEnabled = false;
        try { await _app.DeletePiperAsync(onlyUnused); }
        finally { button.IsEnabled = true; }
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
