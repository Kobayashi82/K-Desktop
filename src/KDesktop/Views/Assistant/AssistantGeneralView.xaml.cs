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
/// thinks with (stage 3.2), with the one its package suggests when it is not here. Until 5.0.112 this was in each row of
/// the list. For K-Desktop as well.
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
            brain?.State, brain?.Model, string.Join(',', brain?.Models?.Select(m => m.Name) ?? []), _app.AssistantModelDownload is { Running: true }, _suggestion?.Model.Name);
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
        _filling = false;
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
        var key = string.Join(',', _app.AssistantBrain.Models?.Select(m => m.Name) ?? []) + "|" + installed.Id + installed.Manifest.Version;
        if (key == _suggestionFor) return;
        _suggestionFor = key;
        var found = await _app.MissingSuggestedModelAsync(installed.Manifest);
        if (key != _suggestionFor || _installed != installed) return;
        _suggestion = found;
        Refresh();
    }

    /// <summary>The model it suggests: downloaded only because the user asked here (its progress is in Options → Brain); it thinks with it once here.</summary>
    private void OnDownloadModel(object sender, RoutedEventArgs e)
    {
        if (_suggestion is { } model && _app.StartModelDownload(model.Model, model.Projector, _id)) Refresh();
    }
}
