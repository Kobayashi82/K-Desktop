using System.IO;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Voice;
using Microsoft.Win32;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Assistants (stage 1.8): the installed assistants with their icon, version and author;
/// «Add assistant» opens a package's card, and each one can be put on the desktop (several at once, stage 1.11),
/// hidden or removed (asking whether to keep its data), and given its own wake word. Below, who takes what is not
/// addressed to anyone. «Settings» shows its own settings (stage 1.9) in place of the list.
/// </summary>
public partial class AssistantListSection : UserControl
{
    /// <summary>A choice of the «not addressed to anyone» box.</summary>
    private sealed record TargetOption(AssistantTarget Target, string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly App _app = App.Current;
    private bool _filling;
    /// <summary>What of the brain the rows show (its models, the download going on): the list is made again only when it changes.</summary>
    private string? _brainShown;

    public AssistantListSection()
    {
        InitializeComponent();
        SettingsView.Closed += ShowList;
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            _app.AssistantBrainChanged += OnBrainChanged;
            _app.AssistantModelDownloadChanged += OnBrainChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= Refresh;
            _app.AssistantBrainChanged -= OnBrainChanged;
            _app.AssistantModelDownloadChanged -= OnBrainChanged;
        };
    }

    private void Refresh()
    {
        // The assistant whose settings are open was removed or updated.
        if (SettingsView.AssistantId is { } open && SettingsView.IsVisible)
        {
            if (AssistantPackages.Find(open) is { } installed) SettingsView.Refresh(installed);
            else ShowList();
        }
        _filling = true;
        var rows = AssistantLook.Rows(_app);
        var words = _app.Voice.WakeWords.Words.ToList();
        var owners = _app.Config.Settings.AssistantWakeWords;
        foreach (var row in rows)
        {
            List<WakeOption> options = [new(null, Loc.T("assistantOptions.wakeNone"))];
            foreach (var word in words)
            {
                // A word of another assistant says whose it is: choosing it here takes it from that one.
                var owner = owners.FirstOrDefault(p => p.Value == word.Id).Key;
                options.Add(new(word.Id, owner is null || owner == row.Id ? word.Name
                    : owner == Core.Assistant.AssistantTargeting.AnyAssistant ? Loc.T("assistantOptions.wakeOfAny", word.Name)
                    : Loc.T("assistantOptions.wakeOf", word.Name, App.AssistantName(owner))));
            }
            row.WakeOptions = options;
            FillModels(row);
        }
        LookUpSuggestions(rows);
        _brainShown = BrainShown();
        AssistantList.ItemsSource = rows;
        EmptyPanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModuleOffText.Visibility = rows.Count > 0 && !_app.Config.Settings.AssistantEnabled ? Visibility.Visible : Visibility.Collapsed;

        var s = _app.Config.Settings;
        List<TargetOption> targets =
        [
            new(AssistantTarget.LastTalkedTo, null, Loc.T("assistantOptions.targetLast")),
            new(AssistantTarget.Random, null, Loc.T("assistantOptions.targetRandom")),
            .. rows.Select(r => new TargetOption(AssistantTarget.Fixed, r.Id, r.Name)),
        ];
        TargetBox.ItemsSource = targets;
        TargetBox.SelectedItem = targets.FirstOrDefault(t => t.Target == s.AssistantTarget && (t.Target != AssistantTarget.Fixed || t.Id == s.AssistantTargetId))
                                 ?? targets[0];
        TargetCard.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _filling = false;
    }

    /// <summary>The key of <see cref="_brainShown"/>: the models the server has and the download going on.</summary>
    private string BrainShown() => (_app.AssistantBrain is { } brain ? $"{brain.State}|{brain.Model}|{string.Join(',', brain.Models?.Select(m => m.Name) ?? [])}" : string.Empty)
        + $"|{_app.AssistantModelDownload is { Running: true } d}|{string.Join(',', _suggestions.Select(p => $"{p.Key}:{p.Value?.Model.File}"))}";

    private void OnBrainChanged()
    {
        if (BrainShown() != _brainShown) Refresh();
    }

    /// <summary>The model each assistant suggests that is not here (looked up in Hugging Face once per state of the brain).</summary>
    private readonly Dictionary<string, App.SuggestedModel?> _suggestions = new(StringComparer.Ordinal);
    private string? _suggestionsFor;

    /// <summary>Looks up what each assistant suggests again when the models of the brain changed; the list is made again with it.</summary>
    private async void LookUpSuggestions(IReadOnlyList<AssistantRow> rows)
    {
        var key = string.Join(',', _app.AssistantBrain?.Models?.Select(m => m.Name) ?? []) + "|" + string.Join(',', rows.Select(r => r.Id + r.Installed.Manifest.Version));
        if (_app.AssistantBrain is null || key == _suggestionsFor) return;
        _suggestionsFor = key;
        var found = new Dictionary<string, App.SuggestedModel?>(StringComparer.Ordinal);
        foreach (var row in rows) found[row.Id] = await _app.MissingSuggestedModelAsync(row.Installed.Manifest);
        if (key != _suggestionsFor) return;
        _suggestions.Clear();
        foreach (var (id, model) in found) _suggestions[id] = model;
        if (BrainShown() != _brainShown) Refresh();
    }

    /// <summary>
    /// The models one assistant can think with (stage 3.2): the general one first, then all of the brain (with their server
    /// when they are of another one); its own when it is not there says so. And the model it suggests, when it is not here.
    /// </summary>
    private void FillModels(AssistantRow row)
    {
        var brain = _app.AssistantBrain;
        row.HasBrain = !_app.AssistantBrainOff;
        List<ModelOption> options =
        [
            new(string.Empty, brain is { State: "ready", Model: { } general } ? Loc.T("assistantOptions.modelGeneralIs", _app.BrainModelName(general)) : Loc.T("assistantOptions.modelGeneral")),
            .. (brain?.Models ?? []).Select(m => new ModelOption(m.Name, _app.BrainModelName(m.Name))),
        ];
        if (row.ModelName.Length > 0 && Core.Assistant.Brain.ModelRefs.Find(options.Skip(1).Select(o => o.Value), row.ModelName) is { } same)
            row.ModelName = same;
        else if (row.ModelName.Length > 0)
            options.Add(new ModelOption(row.ModelName, brain is null ? _app.BrainModelName(row.ModelName)
                : Loc.T("assistantOptions.modelMissing", _app.BrainModelName(row.ModelName))));
        row.ModelOptions = options;

        row.SuggestedModel = _suggestions.GetValueOrDefault(row.Id);
        var downloading = _app.AssistantModelDownload is { Running: true } download ? download : null;
        row.CanDownloadSuggested = row.SuggestedModel is not null && downloading is null;
        row.SuggestedModelText = row.SuggestedModel is not { } suggested ? string.Empty
            : downloading is not null && downloading.Name == suggested.Model.Name ? Loc.T("assistantOptions.modelDownloading", suggested.Model.Name)
            : Loc.T("assistantOptions.modelSuggested", suggested.Model.Name, (suggested.Size / 1e9).ToString("0.0", Loc.Instance.Culture));
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || sender is not ComboBox { DataContext: AssistantRow row, SelectedItem: ModelOption option } || option.Value == row.ModelName) return;
        // After this event: the list is made again when it changes.
        Dispatcher.BeginInvoke(() => _app.SetAssistantModel(row.Id, option.Value));
    }

    /// <summary>The model it suggests: downloaded only because the user asked here (its progress is in Options → Brain); it thinks with it once here.</summary>
    private void OnDownloadModel(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow { SuggestedModel: { } model } row } && _app.StartModelDownload(model.Model, model.Projector, row.Id))
            Refresh();
    }

    /// <summary>The list (null) or an assistant's settings (its id) is shown now: the tree of Options follows.</summary>
    public event Action<string?>? Shown;

    /// <summary>Shows an installed assistant's settings in place of the list (<c>--page settings/assistants/&lt;id&gt;</c>).</summary>
    public void ShowSettings(string id)
    {
        if (AssistantPackages.Find(id) is not { } installed)
        {
            ShowList();
            return;
        }
        SettingsView.Show(installed);
        ListCard.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Shown?.Invoke(id);
    }

    /// <summary>The assistant shown goes to its «Personalidad y voz».</summary>
    public void ShowPersonal()
    {
        if (SettingsView.Visibility == Visibility.Visible) SettingsView.ShowPersonal();
    }

    public void ShowList()
    {
        SettingsView.Visibility = Visibility.Collapsed;
        ListCard.Visibility = Visibility.Visible;
        Shown?.Invoke(null);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow row }) ShowSettings(row.Id);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("assistantOptions.addTitle"),
            Filter = Loc.T("assistant.fileType") + $"|*{AssistantPackages.Extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
                ? Path.Combine(home, "Downloads") : string.Empty,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _app.OpenAssistantPackage(dlg.FileName);
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow row }) _app.ShowAssistant(row.Id);
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow row }) _app.HideAssistant(row.Id);
    }

    private void OnTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || TargetBox.SelectedItem is not TargetOption option) return;
        var s = _app.Config.Settings;
        s.AssistantTarget = option.Target;
        s.AssistantTargetId = option.Id;
    }

    private void OnWakeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || sender is not ComboBox { DataContext: AssistantRow row, SelectedItem: WakeOption option } || option.Id == row.WakeWordId) return;
        // After this event: the list is made again when it changes.
        Dispatcher.BeginInvoke(() => _app.SetAssistantWakeWord(row.Id, option.Id));
    }

    /// <summary>Records a new wake word (with the name the assistant suggests) and gives it to this assistant.</summary>
    private void OnRecordWake(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AssistantRow row }) return;
        if (!VoiceModels.AllInstalled || _app.Voice.Models.IsDownloading)
        {
            _app.Ui.ShowOsd(Loc.T("wake.needModels"), OsdKind.Info);
            return;
        }
        if (Dialogs.WakeWordDialog.Record(Window.GetWindow(this), suggestedName: row.SuggestedWakeWord) is { } word)
            _app.SetAssistantWakeWord(row.Id, word.Id);
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AssistantRow row } button) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("assistantOptions.removeTitle", row.Name),
            Width = 520,
            MaxWidth = 520,
            Content = Loc.T("assistantOptions.removeQuestion", row.Name),
            PrimaryButtonText = Loc.T("assistantOptions.removeKeep"),
            SecondaryButtonText = Loc.T("assistantOptions.removeAll"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        var answer = await box.ShowDialogAsync();
        if (answer is not (Wpf.Ui.Controls.MessageBoxResult.Primary or Wpf.Ui.Controls.MessageBoxResult.Secondary)) return;

        // Leaving the desktop takes a moment (its exit): the row waits meanwhile.
        button.IsEnabled = false;
        try
        {
            await _app.UninstallAssistantAsync(row.Id, keepData: answer == Wpf.Ui.Controls.MessageBoxResult.Primary);
            _app.Ui.ShowOsd(Loc.T("assistantOptions.removed", row.Name), OsdKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Ui.ShowOsd(Loc.T("assistantOptions.removeFailed", row.Name, ex.Message), OsdKind.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
