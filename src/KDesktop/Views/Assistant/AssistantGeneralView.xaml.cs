using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Options → Assistants → one assistant → «General» (5.0.113): its wake word (a word has one owner: choosing one of another
/// takes it), its key to talk (refused while another assistant, a command or K-Desktop answers it) and the model it
/// thinks with (stage 3.2) and the one it thinks harder with (stage 13.10), each with the one its package suggests when
/// it is not here. Until 5.0.112 this was in each row of
/// the list. For K-Desktop as well. And, for an assistant with a body, the settings every assistant has the same way
/// (stage 13.13) that its manifest takes.
/// </summary>
public partial class AssistantGeneralView : UserControl
{
    private readonly App _app = App.Current;
    private string _id = AssistantTargeting.KDesktop;
    private InstalledAssistant? _installed;
    private bool _filling;
    private string? _capturing;
    /// <summary>The key last refused, with who has it (shown until another one is chosen).</summary>
    private string? _refused;

    /// <summary>The model its package suggests when it is not here (looked up in Hugging Face once per state of the brain).</summary>
    private App.SuggestedModel? _suggestion;
    private string? _suggestionFor;

    /// <summary>The model its package suggests to think harder with, when it is not here (stage 13.10).</summary>
    private App.SuggestedModel? _thinkSuggestion;

    public AssistantGeneralView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            _app.AssistantBrainChanged += Refresh;
            _app.AssistantModelDownloadChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= Refresh;
            _app.AssistantBrainChanged -= Refresh;
            _app.AssistantModelDownloadChanged -= Refresh;
        };
    }

    /// <summary>Shows an assistant's (<paramref name="installed"/> null: K-Desktop's).</summary>
    public void Show(InstalledAssistant? installed)
    {
        _installed = installed;
        _id = installed?.Id ?? AssistantTargeting.KDesktop;
        _refused = null;
        _suggestion = null;
        _thinkSuggestion = null;
        _suggestionFor = null;
        _shown = null;
        Refresh();
    }

    private string WhoName => App.AssistantName(_id);

    /// <summary>What it shows, to make it again only when that changes (a list made again closes when it is open).</summary>
    private string? _shown;

    private string Shown()
    {
        var s = _app.Config.Settings;
        var brain = _app.AssistantBrain;
        return string.Join('|', _id, string.Join(',', _app.Voice.WakeWords.Words.Select(w => $"{w.Id}:{w.Name}:{_app.WakeOwnerOf(w.Id)}")),
            s.AssistantWakeWords.GetValueOrDefault(_id), _app.TalkHotkeyOf(_id), s.AssistantModels.GetValueOrDefault(_id), _app.AssistantBrainOff,
            brain?.State, brain?.Model, string.Join(',', brain?.Models?.Select(m => m.Name) ?? []), _app.AssistantModelDownload is { Running: true } download ? download.Name : null,
            _suggestion?.Model.Name, s.AssistantThinkModels.GetValueOrDefault(_id), s.AssistantBrainThinkModel, _thinkSuggestion?.Model.Name);
    }

    private void Refresh()
    {
        if (!IsLoaded) return;
        var shown = Shown();
        LookUpSuggestion();
        if (shown == _shown) return;
        _shown = shown;
        _filling = true;
        CallIntro.Text = Loc.T("assistantGeneral.callIntro", WhoName);
        FillWake();
        ShowTalkKey();
        FillModels();
        FillCommon();
        _filling = false;
    }

    // ── on the desktop ───────────────────────────────────────────────────

    /// <summary>The common settings its manifest takes (K-Desktop, without a body, has none).</summary>
    private void FillCommon()
    {
        var manifest = _installed?.Manifest;
        bool Takes(string key) => manifest?.Takes(key) == true;
        SizeRow.Visibility = Takes(AssistantCommon.SizeKey) ? Visibility.Visible : Visibility.Collapsed;
        VolumeRow.Visibility = Takes(AssistantCommon.VolumeKey) ? Visibility.Visible : Visibility.Collapsed;
        LivelinessRow.Visibility = Takes(AssistantCommon.LivelinessKey) ? Visibility.Visible : Visibility.Collapsed;
        DraggingRow.Visibility = Takes(AssistantCommon.DraggingKey) ? Visibility.Visible : Visibility.Collapsed;
        // One that draws what it says itself (stage 13.7) can be told to use K-Desktop's bubble instead.
        var ownSpeech = manifest?.OwnSpeech == true;
        BubbleRow.Visibility = ownSpeech ? Visibility.Visible : Visibility.Collapsed;
        CommonCard.Visibility = ownSpeech || AssistantCommon.Keys.Any(Takes) ? Visibility.Visible : Visibility.Collapsed;

        var common = _app.AssistantCommonOf(_id);
        SizeSlider.Value = Math.Round(common.Size * 100);
        VolumeSlider.Value = Math.Round(common.Volume * 100);
        LivelinessSlider.Value = Math.Round(common.Liveliness * 100);
        DraggingSwitch.IsChecked = common.Dragging;
        BubbleSwitch.IsChecked = common.KDesktopBubble;
        ShowCommonValues();
    }

    private void ShowCommonValues()
    {
        var culture = Loc.Instance.Culture;
        SizeText.Text = (SizeSlider.Value / 100).ToString("P0", culture);
        VolumeText.Text = (VolumeSlider.Value / 100).ToString("P0", culture);
        LivelinessText.Text = (LivelinessSlider.Value / 100).ToString("P0", culture);
    }

    private void OnCommonSlid(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveCommon();

    private void OnCommonChanged(object sender, RoutedEventArgs e) => SaveCommon();

    private void SaveCommon()
    {
        if (!IsLoaded) return;
        ShowCommonValues();
        if (_filling) return;
        _app.SetAssistantCommon(_id, new AssistantCommon
        {
            Size = SizeSlider.Value / 100,
            Volume = VolumeSlider.Value / 100,
            Liveliness = LivelinessSlider.Value / 100,
            Dragging = DraggingSwitch.IsChecked == true,
            KDesktopBubble = BubbleSwitch.IsChecked == true,
        });
    }

    // ── its word ─────────────────────────────────────────────────────────

    private void FillWake()
    {
        List<WakeOption> options = [new(null, Loc.T("assistantOptions.wakeNone"))];
        foreach (var word in _app.Voice.WakeWords.Words)
        {
            // A word of another owner says whose it is: choosing it here takes it from that one.
            var owner = _app.WakeOwnerOf(word.Id);
            options.Add(new(word.Id, owner is null || owner == _id ? word.Name
                : owner == WakeOwners.Commands ? Loc.T("assistantOptions.wakeOfKDesktop", word.Name)
                : Loc.T("assistantOptions.wakeOf", word.Name, App.AssistantName(owner))));
        }
        WakeBox.ItemsSource = options;
        var mine = _app.Config.Settings.AssistantWakeWords.GetValueOrDefault(_id);
        WakeBox.SelectedItem = options.FirstOrDefault(o => o.Id == mine) ?? options[0];
    }

    private void OnWakeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || WakeBox.SelectedItem is not WakeOption option || option.Id == _app.Config.Settings.AssistantWakeWords.GetValueOrDefault(_id)) return;
        // After this event: the box is filled again when it changes.
        var id = _id;
        Dispatcher.BeginInvoke(() =>
        {
            if (option.Id is null) _app.SetAssistantWakeWord(id, null);
            else _app.SetWakeOwner(option.Id, id);
        });
    }

    /// <summary>Records a new wake word (with the name the assistant suggests) and gives it to this one.</summary>
    private void OnRecordWake(object sender, RoutedEventArgs e)
    {
        if (!VoiceModels.AllInstalled || _app.Voice.Models.IsDownloading)
        {
            _app.Ui.ShowOsd(Loc.T("wake.needModels"), OsdKind.Info);
            return;
        }
        var suggested = _installed?.Manifest.WakeWord is { Length: > 0 } word ? word : WhoName;
        if (Dialogs.WakeWordDialog.Record(Window.GetWindow(this), suggestedName: suggested) is { } recorded)
            _app.SetAssistantWakeWord(_id, recorded.Id);
    }

    // ── its key to talk ──────────────────────────────────────────────────

    private void ShowTalkKey()
    {
        var hotkey = _app.TalkHotkeyOf(_id);
        TalkKeyButton.Content = _capturing ?? (hotkey.Length == 0 ? Loc.T("settings.voiceNoHotkey") : hotkey);
        TalkKeyClear.Visibility = hotkey.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TalkHint.Text = _refused ?? Loc.T("assistantGeneral.talkKeyHint", WhoName);
        TalkHint.SetResourceReference(TextBlock.ForegroundProperty, _refused is null ? "TextFillColorSecondaryBrush" : "Kd.Danger");
    }

    private void OnCaptureTalkKey(object sender, RoutedEventArgs e)
    {
        _capturing = Loc.T("commands.pressHotkey");
        ShowTalkKey();
        HotkeyCapture.Start(
            preview =>
            {
                _capturing = preview.Length > 0 ? preview + "…" : Loc.T("commands.pressHotkey");
                ShowTalkKey();
            },
            hotkey =>
            {
                _capturing = null;
                if (hotkey is not null && Core.Input.Hotkey.Normalize(hotkey) is { Length: > 0 } key)
                    _refused = _app.SetTalkHotkey(_id, key) is { Count: > 0 } users ? Loc.T("assistantGeneral.talkKeyTaken", key, string.Join(", ", users)) : null;
                _shown = null;
                ShowTalkKey();
            },
            Window.GetWindow(this));
    }

    private void OnClearTalkKey(object sender, RoutedEventArgs e)
    {
        _refused = null;
        _app.SetTalkHotkey(_id, string.Empty);
        ShowTalkKey();
    }

    // ── the model it thinks with ─────────────────────────────────────────

    /// <summary>
    /// The general one first, then all of the brain (with their server when they are of another one); its own when it is
    /// not there says so. Not with «no AI».
    /// </summary>
    private void FillModels()
    {
        BrainCard.Visibility = _app.AssistantBrainOff ? Visibility.Collapsed : Visibility.Visible;
        var brain = _app.AssistantBrain;
        var own = _app.Config.Settings.AssistantModels.GetValueOrDefault(_id) ?? string.Empty;
        List<ModelOption> options =
        [
            new(string.Empty, brain is { State: "ready", Model: { } general } ? Loc.T("assistantOptions.modelGeneralIs", _app.BrainModelName(general)) : Loc.T("assistantOptions.modelGeneral")),
            .. (brain?.Models ?? []).Select(m => new ModelOption(m.Name, _app.BrainModelName(m.Name))),
        ];
        if (own.Length > 0 && Core.Assistant.Brain.ModelRefs.Find(options.Skip(1).Select(o => o.Value), own) is { } same) own = same;
        else if (own.Length > 0)
            options.Add(new ModelOption(own, brain is null ? _app.BrainModelName(own) : Loc.T("assistantOptions.modelMissing", _app.BrainModelName(own))));
        ModelBox.ItemsSource = options;
        ModelBox.SelectedItem = options.FirstOrDefault(o => string.Equals(o.Value, own, StringComparison.OrdinalIgnoreCase)) ?? options[0];

        var downloading = _app.AssistantModelDownload is { Running: true } download ? download : null;
        SuggestedRow.Visibility = _suggestion is null ? Visibility.Collapsed : Visibility.Visible;
        DownloadButton.Visibility = _suggestion is not null && downloading is null ? Visibility.Visible : Visibility.Collapsed;
        SuggestedText.Text = _suggestion is not { } suggested ? string.Empty
            : downloading is not null && downloading.Name == suggested.Model.Name ? Loc.T("assistantOptions.modelDownloading", suggested.Model.Name)
            : Loc.T("assistantOptions.modelSuggested", suggested.Model.Name, (suggested.Size / 1e9).ToString("0.0", Loc.Instance.Culture));
        FillThink(brain, downloading);
    }

    /// <summary>
    /// The model it thinks harder with: as everyone (Options → Brain → «Para pensar más») or one of the brain; hidden while
    /// thinking harder is off for everyone.
    /// </summary>
    private void FillThink(Core.Assistant.Pipe.BrainStateMessage? brain, App.ModelDownload? downloading)
    {
        var general = _app.Config.Settings.AssistantBrainThinkModel;
        var off = general == Core.Assistant.Brain.ModelRefs.None;
        ThinkRow.Visibility = off ? Visibility.Collapsed : Visibility.Visible;
        var own = _app.Config.Settings.AssistantThinkModels.GetValueOrDefault(_id) ?? string.Empty;
        List<ModelOption> options =
        [
            new(string.Empty, general.Length == 0 ? Loc.T("assistantGeneral.thinkGeneralSame") : Loc.T("assistantOptions.modelGeneralIs", _app.BrainModelName(general))),
            .. (brain?.Models ?? []).Select(m => new ModelOption(m.Name, _app.BrainModelName(m.Name))),
        ];
        if (own.Length > 0 && Core.Assistant.Brain.ModelRefs.Find(options.Skip(1).Select(o => o.Value), own) is { } same) own = same;
        else if (own.Length > 0)
            options.Add(new ModelOption(own, brain is null ? _app.BrainModelName(own) : Loc.T("assistantOptions.modelMissing", _app.BrainModelName(own))));
        ThinkBox.ItemsSource = options;
        ThinkBox.SelectedItem = options.FirstOrDefault(o => string.Equals(o.Value, own, StringComparison.OrdinalIgnoreCase)) ?? options[0];

        var suggested = off ? null : _thinkSuggestion;
        ThinkSuggestedRow.Visibility = suggested is null ? Visibility.Collapsed : Visibility.Visible;
        ThinkDownloadButton.Visibility = suggested is not null && downloading is null ? Visibility.Visible : Visibility.Collapsed;
        ThinkSuggestedText.Text = suggested is null ? string.Empty
            : downloading is not null && downloading.Name == suggested.Model.Name ? Loc.T("assistantOptions.modelDownloading", suggested.Model.Name)
            : Loc.T("assistantGeneral.thinkSuggested", suggested.Model.Name, (suggested.Size / 1e9).ToString("0.0", Loc.Instance.Culture));
    }

    private void OnThinkChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ThinkBox.SelectedItem is not ModelOption option) return;
        if (string.Equals(option.Value, _app.Config.Settings.AssistantThinkModels.GetValueOrDefault(_id) ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return;
        var id = _id;
        Dispatcher.BeginInvoke(() => _app.SetAssistantThinkModel(id, option.Value));
    }

    /// <summary>The model it suggests to think harder with: downloaded only because the user asked here; it thinks harder with it once here.</summary>
    private void OnDownloadThinkModel(object sender, RoutedEventArgs e)
    {
        if (_thinkSuggestion is { } model && _app.StartModelDownload(model.Model, model.Projector, _id, forThinking: true)) Refresh();
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ModelBox.SelectedItem is not ModelOption option) return;
        if (string.Equals(option.Value, _app.Config.Settings.AssistantModels.GetValueOrDefault(_id) ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return;
        var id = _id;
        Dispatcher.BeginInvoke(() => _app.SetAssistantModel(id, option.Value));
    }

    /// <summary>What its package suggests (K-Desktop has none), looked up again when the models of the brain change.</summary>
    private async void LookUpSuggestion()
    {
        if (_installed is not { } installed || _app.AssistantBrain is null) return;
        var key = string.Join(',', _app.AssistantBrain.Models?.Select(m => m.Name) ?? []) + "|" + installed.Id + installed.Manifest.Version
                  + "|" + (_app.Config.Settings.AssistantBrainThinkModel == Core.Assistant.Brain.ModelRefs.None);
        if (key == _suggestionFor) return;
        _suggestionFor = key;
        var found = await _app.MissingSuggestedModelAsync(installed.Manifest);
        var think = await _app.MissingSuggestedModelAsync(installed.Manifest, thinking: true);
        if (key != _suggestionFor || _installed != installed) return;
        _suggestion = found;
        _thinkSuggestion = think;
        Refresh();
    }

    /// <summary>The model it suggests: downloaded only because the user asked here (its progress is in Options → Brain); it thinks with it once here.</summary>
    private void OnDownloadModel(object sender, RoutedEventArgs e)
    {
        if (_suggestion is { } model && _app.StartModelDownload(model.Model, model.Projector, _id)) Refresh();
    }
}
