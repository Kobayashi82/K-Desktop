using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;
using KDesktop.Views.Pages;

namespace KDesktop.Views.Assistant;

/// <summary>
/// What one assistant has of its own (5.0.105): the language it speaks in and its personality (its package's, shown and
/// editable, with «Restablecer») on «Personalidad», and its voice on «Voz» (two pages since 5.0.289, <see cref="ShowPart"/>).
/// Every choice has «Global», the one of Options → Assistant module. Saved at once; the process gets it in the <c>personal</c> section. With <see cref="ShowSystem"/> it is K-Desktop's
/// own, the assistant of the system (5.0.112; Options → Assistants → K-Desktop).
/// </summary>
public partial class AssistantPersonalView : UserControl
{
    private sealed record Option(string Value, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly string[] Engines = ["auto", "kokoro", "piper", "windows", "beeps", "off"];

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _promptSave = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private InstalledAssistant? _assistant;
    private bool _system;
    private string _packagePrompt = string.Empty;
    private bool _filling;

    public AssistantPersonalView()
    {
        InitializeComponent();
        _promptSave.Tick += (_, _) => SavePrompt();
        Loaded += (_, _) =>
        {
            _app.AssistantVoiceChanged += RefreshVoice;
            _app.AssistantChanged += RefreshVoice;
            RefreshVoice();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantVoiceChanged -= RefreshVoice;
            _app.AssistantChanged -= RefreshVoice;
            SavePrompt();
        };
    }

    /// <summary>Shows what <paramref name="assistant"/> has of its own.</summary>
    public void Show(InstalledAssistant assistant)
    {
        SavePrompt();
        _assistant = assistant;
        _system = false;
        Refresh();
    }

    /// <summary>Shows what K-Desktop, the assistant of the system, has of its own.</summary>
    public void ShowSystem()
    {
        SavePrompt();
        _assistant = null;
        _system = true;
        Refresh();
    }

    /// <summary>Shows its language and personality, or (<paramref name="voice"/>) its voice.</summary>
    public void ShowPart(bool voice)
    {
        _voicePart = voice;
        SystemCard.Visibility = _system && !voice ? Visibility.Visible : Visibility.Collapsed;
        LanguageCard.Visibility = PersonalityCard.Visibility = voice ? Visibility.Collapsed : Visibility.Visible;
        VoiceCard.Visibility = voice ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _voicePart;

    private void Refresh()
    {
        ShowPart(_voicePart);
        LanguageHint.Text = Loc.T(_system ? "assistantPersonal.languageHintSystem" : "assistantPersonal.languageHint");
        RefreshPersonality(force: true);
        RefreshLanguage();
        RefreshVoice();
    }

    /// <summary>Whether it shows someone (an assistant, or K-Desktop).</summary>
    private bool Shown => _assistant is not null || _system;

    private string WhoName => _assistant?.Manifest.Name ?? "K-Desktop";

    /// <summary>Saves the personality being written, if it is still waiting.</summary>
    public void Flush() => SavePrompt();

    private string Id => _system ? AssistantTargeting.KDesktop : _assistant!.Id;

    private AssistantPersonal Own => _app.AssistantPersonalOf(Id);

    private string SpokenLanguage => _app.LanguageOfAssistant(Id);

    private static string LanguageName(string code) =>
        Loc.Available().FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Name ?? CultureInfo.GetCultureInfo(code).NativeName;

    private static string AsOthers(string what) => Loc.T("assistantPersonal.asOthers", what);

    private void Save(Func<AssistantPersonal, AssistantPersonal> change)
    {
        if (!Shown) return;
        var now = change(Own);
        if (now != Own) _app.SetAssistantPersonal(Id, now);
    }

    // ── personality ──────────────────────────────────────────────────────

    /// <param name="force">Also while it is being written (another assistant, another language).</param>
    private void RefreshPersonality(bool force)
    {
        if (!Shown) return;
        var language = SpokenLanguage;
        // Its package's, in the language it speaks in (the module's, also in that language, when it brings none or for K-Desktop).
        using (Loc.Instance.Use(language)) _packagePrompt = _assistant is { } assistant ? App.PackagePrompt(assistant, language) : AssistantPersona.Module().Prompt;
        var own = Own.Prompt;
        if (force || !PromptBox.IsKeyboardFocusWithin)
        {
            _filling = true;
            PromptBox.Text = own ?? _packagePrompt;
            _filling = false;
        }
        PersonalityState.Text = own is not null ? Loc.T("assistantPersonal.personalityOwn")
            : Loc.T(_system ? "assistantPersonal.personalityModule" : "assistantPersonal.personalityPackage", LanguageName(language));
        PersonalityReset.IsEnabled = own is not null;
    }

    private void OnPromptChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling) return;
        _promptSave.Stop();
        _promptSave.Start();
    }

    /// <summary>What is written is its personality; empty or the same as its package's: its package's.</summary>
    private void SavePrompt()
    {
        if (!_promptSave.IsEnabled || !Shown) return;
        _promptSave.Stop();
        var text = PromptBox.Text.Trim();
        string? prompt = text.Length == 0 || text == _packagePrompt.Trim() ? null : text;
        Save(o => o with { Prompt = prompt });
        RefreshPersonality(force: false);
    }

    private void OnResetPersonality(object sender, RoutedEventArgs e)
    {
        _promptSave.Stop();
        Save(o => o with { Prompt = null });
        RefreshPersonality(force: true);
    }

    // ── language ─────────────────────────────────────────────────────────

    private void RefreshLanguage()
    {
        if (!Shown) return;
        _filling = true;
        List<Option> languages = [new(string.Empty, AsOthers(LanguageName(_app.AssistantLanguage))), .. Loc.Available().Select(l => new Option(l.Code, l.Name))];
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = languages.FirstOrDefault(l => l.Value == Own.Language) ?? languages[0];
        _filling = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || LanguageBox.SelectedItem is not Option option) return;
        SavePrompt();
        Dispatcher.BeginInvoke(() =>
        {
            Save(o => o with { Language = option.Value });
            // Its package's personality in the new language; its voice, of the new language.
            RefreshPersonality(force: true);
            RefreshVoice();
        });
    }

    // ── voice ────────────────────────────────────────────────────────────

    private void RefreshVoice()
    {
        if (!Shown || !IsLoaded) return;
        _filling = true;
        var s = _app.Config.Settings;
        var own = Own;
        var language = SpokenLanguage;
        var languageName = LanguageName(language);

        List<Option> engines = [new(string.Empty, AsOthers(EngineName(s.AssistantVoiceEngine))), .. Engines.Select(e => new Option(e, EngineName(e)))];
        EngineBox.ItemsSource = engines;
        EngineBox.SelectedItem = engines.FirstOrDefault(e => e.Value == own.Engine) ?? engines[0];

        // Without a profile for all of them, each assistant uses its package's; K-Desktop has none: «Natural».
        var general = s.AssistantVoiceProfile is { Length: > 0 } fixedProfile ? ProfileName(fixedProfile)
            : _system ? ProfileName(VoiceProfile.Default.Id) : Loc.T("assistantVoice.profileOwn");
        List<Option> profiles =
        [
            new(string.Empty, AsOthers(general)),
            .. _system ? [] : new[] { new Option(AssistantPersonal.OwnProfile, Loc.T("assistantPersonal.profilePackage")) },
            .. VoiceProfiles.All.Select(p => new Option(p.Id, VoiceProfiles.NameOf(p))),
        ];
        ProfileBox.ItemsSource = profiles;
        ProfileBox.SelectedItem = profiles.FirstOrDefault(p => p.Value == own.Profile) ?? profiles[0];

        // Kokoro: the voices of its language («Como los demás» is the general one if it is of that language, else its first).
        var supported = KokoroModels.Supports(language);
        KokoroRows.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (supported)
        {
            var installed = KokoroModels.InstalledVoices(language);
            Option Named(string v) => new(v, KokoroModels.IsInFolders(v) ? Loc.T("assistantVoice.voiceOwn", AssistantVoiceSection.VoiceTitle(v, language))
                : installed.Contains(v) ? AssistantVoiceSection.VoiceTitle(v, language)
                : Loc.T("assistantVoice.voiceMissing", AssistantVoiceSection.VoiceTitle(v, language)));
            var parts = own.KokoroVoice.Length > 0 ? KokoroModels.Parts(KokoroModels.VoiceOrDefault(language, own.KokoroVoice)) : [];
            var first = parts.Count > 0 ? parts[0].Name : string.Empty;
            var second = parts.Count > 1 ? parts[1].Name : null;
            List<Option> voices =
            [
                new(string.Empty, AsOthers(AssistantVoiceSection.VoiceTitle(KokoroModels.VoiceOrDefault(language, s.AssistantVoiceKokoro), language))),
                .. KokoroModels.Voices(language).Select(Named),
            ];
            KokoroVoiceBox.ItemsSource = voices;
            KokoroVoiceBox.SelectedItem = voices.FirstOrDefault(v => v.Value == first) ?? voices[0];
            MixVoiceRow.Visibility = first.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            List<Option> mixes = [new(string.Empty, Loc.T("assistantVoice.mixNone")), .. KokoroModels.Voices(language).Where(v => v != first).Select(Named)];
            MixVoiceBox.ItemsSource = mixes;
            MixVoiceBox.SelectedItem = mixes.FirstOrDefault(v => v.Value == (second ?? string.Empty)) ?? mixes[0];
            MixRow.Visibility = second is null ? Visibility.Collapsed : Visibility.Visible;
            if (second is not null)
            {
                MixSlider.Value = Math.Clamp(Math.Round(parts[1].Weight / parts.Sum(p => p.Weight) * 20) * 5, 10, 90);
                MixLabel.Text = Loc.T("assistantVoice.mixShareOf", KokoroModels.DisplayName(second), KokoroModels.DisplayName(first));
                MixValue.Text = $"{MixSlider.Value:0} %";
            }
            KokoroHint.Text = !KokoroModels.ModelInstalled ? Loc.T("assistantPersonal.kokoroNotDownloaded")
                : _app.KokoroDownload.IsDownloading ? Loc.T("assistantPersonal.kokoroDownloading")
                : Loc.T("assistantVoice.kokoroVoiceHint");
        }

        // Piper: the voices of its language («Como los demás» is the general one if it is of that language, else its default).
        var ownPiper = own.PiperVoice.Length > 0 ? PiperModels.VoiceOrDefault(language, own.PiperVoice) : string.Empty;
        List<Option> piperVoices =
        [
            new(string.Empty, AsOthers(AssistantVoiceSection.PiperTitle(PiperModels.VoiceOrDefault(language, s.AssistantVoicePiper)))),
            .. PiperModels.Voices(language).Where(v => AssistantVoiceSection.Shown(v, ownPiper)).Select(v => new Option(v.Key, AssistantVoiceSection.PiperOption(v))),
        ];
        PiperVoiceBox.ItemsSource = piperVoices;
        PiperVoiceBox.SelectedItem = piperVoices.FirstOrDefault(v => v.Value == PiperModels.KeyOf(ownPiper)) ?? piperVoices[0];
        var speakers = ownPiper.Length > 0 ? PiperModels.Find(ownPiper)?.Speakers ?? [] : [];
        PiperSpeakerRow.Visibility = speakers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        List<Option> who = [.. speakers.Select(name => new Option(name, AssistantVoiceSection.SpeakerName(name, alone: true)))];
        PiperSpeakerBox.ItemsSource = who;
        PiperSpeakerBox.SelectedItem = who.FirstOrDefault(o => o.Value == PiperModels.SpeakerOf(ownPiper)) ?? who.FirstOrDefault();
        PiperHint.Text = _app.PiperDownload.IsDownloading ? Loc.T("assistantPersonal.kokoroDownloading")
            : PiperModels.Installed(language, _app.PiperVoiceOf(Id)) ? Loc.T("assistantVoice.piperVoiceHint")
            : Loc.T("assistantPersonal.piperNotDownloaded");

        var state = _app.AssistantVoice;
        var windowsVoices = state?.WindowsVoicesOf?.GetValueOrDefault(language) ?? (language == _app.AssistantLanguage ? state?.WindowsVoices : null) ?? [];
        List<Option> windows =
        [
            new(string.Empty, AsOthers(s.AssistantVoiceWindows is { Length: > 0 } w && windowsVoices.Contains(w) ? w : Loc.T("assistantVoice.windowsFirst"))),
            .. windowsVoices.Order(StringComparer.CurrentCultureIgnoreCase).Select(v => new Option(v, v)),
        ];
        WindowsVoiceBox.ItemsSource = windows;
        WindowsVoiceBox.SelectedItem = windows.FirstOrDefault(v => v.Value == own.WindowsVoice) ?? windows[0];
        WindowsHint.Text = !supported ? Loc.T("assistantPersonal.noKokoro", languageName)
            : state is { } && windowsVoices.Count == 0 ? Loc.T("assistantVoice.windowsNone", languageName)
            : Loc.T("assistantVoice.windowsVoiceHint");

        // Only the settings of who says it aloud: the one chosen, or the one the general «auto» takes for its language.
        var engine = own.Engine.Length > 0 ? own.Engine : s.AssistantVoiceEngine;
        if (engine is not ("kokoro" or "pocket" or "piper" or "windows" or "beeps" or "off"))
            engine = supported && KokoroModels.ModelInstalled ? "kokoro" : PiperModels.Installed(language, _app.PiperVoiceOf(Id)) ? "piper" : "windows";
        KokoroRows.Visibility = supported && engine is "kokoro" or "pocket" ? Visibility.Visible : Visibility.Collapsed;
        PiperRows.Visibility = engine == "piper" ? Visibility.Visible : Visibility.Collapsed;
        // Kokoro does not speak its language: Windows says it (and its hint says why).
        WindowsRow.Visibility = engine == "windows" || (!supported && engine is "kokoro" or "pocket") ? Visibility.Visible : Visibility.Collapsed;
        var silent = engine == "off";
        ProfileRow.Visibility = SpeedRow.Visibility = VolumeRow.Visibility = silent ? Visibility.Collapsed : Visibility.Visible;

        VolumeSwitch.IsChecked = own.Volume is not null;
        VolumeSlider.IsEnabled = own.Volume is not null;
        VolumeSlider.Value = Math.Round((own.Volume ?? s.AssistantVoiceVolume / 100.0) * 100);
        VolumeValue.Text = $"{VolumeSlider.Value:0} %";

        SpeedSwitch.IsChecked = own.Speed is not null;
        SpeedSlider.IsEnabled = own.Speed is not null;
        SpeedSlider.Value = own.Speed ?? s.AssistantVoiceSpeed;
        SpeedValue.Text = SpeedSlider.Value.ToString("0.00", Loc.Instance.Culture) + "×";

        VoiceReset.IsEnabled = own.HasOwnVoice;
        // K-Desktop has nothing to load: its voice is heard while the module runs.
        var canTest = _app.Assistant is { IsRunning: true } && (_system || _app.IsAssistantLoaded(Id));
        TestButton.IsEnabled = StopButton.IsEnabled = canTest;
        TestHint.Text = canTest ? string.Empty : Loc.T(_system ? "assistantPersonal.testHintSystem" : "assistantPersonal.testHint", WhoName);
        _filling = false;
    }

    private static string EngineName(string engine) => engine switch
    {
        "kokoro" or "pocket" => Loc.T("assistantVoice.engineKokoro"),
        "piper" => Loc.T("assistantVoice.enginePiper"),
        "windows" => Loc.T("assistantVoice.engineWindows"),
        "beeps" => Loc.T("assistantVoice.engineBeeps"),
        "off" => Loc.T("assistantVoice.engineOff"),
        _ => Loc.T("assistantVoice.engineAuto"),
    };

    private static string ProfileName(string id) => VoiceProfiles.NameOf(id);

    /// <summary>A list changed: its voice is what the lists say now.</summary>
    private void OnVoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        SaveVoice();
        // After this event: the lists are made again (the mix depends on the voice).
        Dispatcher.BeginInvoke(RefreshVoice);
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || !Shown) return;
        if (ReferenceEquals(sender, MixSlider)) MixValue.Text = $"{MixSlider.Value:0} %";
        else if (ReferenceEquals(sender, VolumeSlider)) VolumeValue.Text = $"{VolumeSlider.Value:0} %";
        else SpeedValue.Text = SpeedSlider.Value.ToString("0.00", Loc.Instance.Culture) + "×";
        SaveVoice();
    }

    private void OnSpeedSwitch(object sender, RoutedEventArgs e)
    {
        if (_filling) return;
        SpeedSlider.IsEnabled = SpeedSwitch.IsChecked == true;
        SaveVoice();
        RefreshVoice();
    }

    private void OnVolumeSwitch(object sender, RoutedEventArgs e)
    {
        if (_filling) return;
        VolumeSlider.IsEnabled = VolumeSwitch.IsChecked == true;
        SaveVoice();
        RefreshVoice();
    }

    private void SaveVoice()
    {
        static string Value(ComboBox box) => (box.SelectedItem as Option)?.Value ?? string.Empty;
        var voice = Value(KokoroVoiceBox);
        var mix = Value(MixVoiceBox);
        var spec = voice.Length == 0 ? string.Empty
            : KokoroModels.Mix(voice, mix.Length > 0 && mix != voice ? mix : null, MixRow.Visibility == Visibility.Visible ? MixSlider.Value / 100 : 0.5);
        // Piper: the voice and, only while it is the same voice, the speaker chosen (another voice starts with its first).
        var piper = Value(PiperVoiceBox);
        var speaker = piper.Length > 0 && PiperModels.KeyOf(Own.PiperVoice) == piper && PiperSpeakerRow.Visibility == Visibility.Visible
            ? Value(PiperSpeakerBox) : string.Empty;
        Save(o => o with
        {
            Engine = Value(EngineBox),
            Profile = Value(ProfileBox),
            KokoroVoice = KokoroRows.Visibility == Visibility.Visible ? spec : o.KokoroVoice,
            PiperVoice = piper.Length == 0 ? string.Empty : PiperModels.Spec(piper, speaker),
            WindowsVoice = Value(WindowsVoiceBox),
            Speed = SpeedSwitch.IsChecked == true ? Math.Round(SpeedSlider.Value, 2) : null,
            Volume = VolumeSwitch.IsChecked == true ? Math.Round(VolumeSlider.Value / 100, 2) : null,
        });
    }

    private void OnResetVoice(object sender, RoutedEventArgs e)
    {
        Save(o => o.WithoutVoice());
        RefreshVoice();
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        if (!Shown) return;
        _app.TestAssistantVoice(App.TestSentence(SpokenLanguage, WhoName), null, Id);
    }

    private void OnStopTest(object sender, RoutedEventArgs e) => _app.StopAssistantVoiceTest();
}
