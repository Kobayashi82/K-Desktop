using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Views.Assistant;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Brain (stage 3.1; its own brain since 5.0.107): what is in memory now —each loaded model with its
/// «Liberar»—, the general model with «Cargar», one model at a time, vision, how long a model stays; every model, those of
/// this PC and those of each other server, each group in its panel (load, free, a model's own llama-server options,
/// delete a downloaded one); the llama-servers and folders of this PC; and the other servers (Ollama or OpenAI compatible:
/// a llama-swap is found out by itself) with «Probar». The work is the assistant process's (downloads are K-Desktop's):
/// this sends <c>brain</c> requests and shows its <c>brainState</c>. While shown, it asks again every few seconds.
/// </summary>
public partial class AssistantBrainSection : UserControl
{
    private sealed record ModelOption(string Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record KindOption(RemoteKind Kind, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>Minutes a model may stay unused in the graphics card (0: until asked).</summary>
    private static readonly int[] KeepChoices = [5, 10, 15, 30, 60, 0];

    private sealed record KeepOption(int Minutes, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>Tokens of reasoning it may spend thinking harder (stage 12): a little, normal, a lot.</summary>
    private static readonly int[] ThinkChoices = [512, BrainConfig.DefaultThinkTokens, 2048];

    private sealed record ThinkOption(int Tokens, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _filling;
    /// <summary>What each list of rows shows, so it is made again only when that changes (a button under the mouse stays).</summary>
    private string? _stateShown, _serversShown, _foldersShown, _modelsShown, _remotesShown;
    /// <summary>The model whose own llama-server options are open for writing (null: none).</summary>
    private string? _argsOpen;
    private TextBox? _argsBox;

    public AssistantBrainSection()
    {
        InitializeComponent();
        KeepBox.ItemsSource = KeepChoices.Select(m => new KeepOption(m, m switch
        {
            0 => Loc.T("brainOptions.keepNever"),
            60 => Loc.T("brainOptions.keepHour"),
            _ => Loc.T("brainOptions.keepMinutes", m),
        })).ToList();
        ThinkTokensBox.ItemsSource = ThinkChoices.Select(t => new ThinkOption(t, Loc.T(t < BrainConfig.DefaultThinkTokens ? "brainOptions.thinkLittle"
            : t > BrainConfig.DefaultThinkTokens ? "brainOptions.thinkMuch" : "brainOptions.thinkNormal"))).ToList();
        NewKindBox.ItemsSource = new[] { RemoteKind.Ollama, RemoteKind.OpenAI }.Select(k => new KindOption(k, KindName(k))).ToList();
        NewKindBox.SelectedIndex = 0;
        _poll.Tick += (_, _) => _app.AskAssistantBrain("check");
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Fill;
            _app.AssistantBrainChanged += Fill;
            _app.AssistantModelDownloadChanged += Fill;
            Fill();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= Fill;
            _app.AssistantBrainChanged -= Fill;
            _app.AssistantModelDownloadChanged -= Fill;
            _poll.Stop();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _poll.Stop();
                return;
            }
            _app.AskAssistantBrain("check");
            _poll.Start();
            Fill();
        };
    }

    public static string KindName(RemoteKind kind) => kind == RemoteKind.OpenAI ? Loc.T("brainOptions.provider.openai") : kind.DisplayName();

    private static CultureInfo Culture => Loc.Instance.Culture;

    public static string Gb(double bytes) => (bytes / 1e9).ToString(bytes >= 10e9 ? "0" : "0.0", Culture);

    private void Fill()
    {
        _filling = true;
        try
        {
            var s = _app.Config.Settings;
            var brain = _app.AssistantBrain;
            OffBox.Visibility = s.AssistantEnabled ? Visibility.Collapsed : Visibility.Visible;
            Body.IsEnabled = ModelsCard.IsEnabled = LocalCard.IsEnabled = RemoteCard.IsEnabled = s.AssistantEnabled;
            CheckButton.IsEnabled = brain is not null;

            OneAtATimeSwitch.IsChecked = s.AssistantBrainOneAtATime;
            VisionSwitch.IsChecked = s.AssistantBrainVision;
            KeepBox.SelectedItem = KeepBox.Items.OfType<KeepOption>().FirstOrDefault(k => k.Minutes == s.AssistantBrainKeepMinutes)
                ?? KeepBox.Items.OfType<KeepOption>().First(k => k.Minutes == BrainConfig.DefaultKeepMinutes);

            FillModelBox(s.AssistantBrainModel, brain);
            FillThinkBox(s.AssistantBrainThinkModel, brain);
            ThinkTokensBox.SelectedItem = ThinkTokensBox.Items.OfType<ThinkOption>().MinBy(t => Math.Abs(t.Tokens - s.AssistantBrainThinkTokens));
            ThinkTokensRow.Visibility = s.AssistantBrainThinkModel == ModelRefs.None ? Visibility.Collapsed : Visibility.Visible;
            FillState(brain);
            FillModels(brain);
            FillDownload();
            FillServers(brain);
            FillFolders();
            FillRemotes(brain);
            FillNewRemote();
        }
        finally
        {
            _filling = false;
        }
    }

    // ── the general model and what is in memory ─────────────────────────

    /// <summary>What a model is, in a few words: its size, whether it sees, and the llama-server it opens with.</summary>
    private string Describe(BrainModel model)
    {
        var parts = new List<string>();
        if (model.SizeBytes > 0) parts.Add(Gb(model.SizeBytes) + " GB");
        else if (model.Billions > 0) parts.Add(Loc.T("brainOptions.billions", model.Billions.ToString("0.#", Culture)));
        if (model.Vision) parts.Add(Loc.T("brainModels.vision"));
        if (!model.Tools) parts.Add(Loc.T("brainOptions.noTools"));
        if (model.Server is { } server && _app.Config.Settings.AssistantBrainServers.Count > 1) parts.Add(ServerLabel(server));
        return string.Join(" · ", parts);
    }

    private void FillModelBox(string chosen, BrainStateMessage? brain)
    {
        var options = new List<ModelOption>
        {
            new(string.Empty, brain is { Automatic: true, Model: { } picked } ? Loc.T("brainOptions.modelAutoIs", _app.BrainModelName(picked)) : Loc.T("brainOptions.modelAuto")),
        };
        foreach (var model in brain?.Models ?? [])
            options.Add(new ModelOption(model.Name, $"{_app.BrainModelName(model.Name)} · {Describe(model)}".TrimEnd(' ', '·')));
        options.Add(new ModelOption(ModelRefs.None, Loc.T("brainOptions.modelNone")));
        var selected = chosen.Length == 0 ? options[0]
            : chosen == ModelRefs.None ? options[^1]
            : ModelRefs.Find(options.Skip(1).Select(o => o.Value).Where(v => v != ModelRefs.None), chosen) is { } found ? options.First(o => o.Value == found)
            : null;
        if (selected is null)
        {
            selected = new ModelOption(chosen, Loc.T("brainOptions.modelNotThere", _app.BrainModelName(chosen)));
            options.Insert(1, selected);
        }
        // «Cargar» only while the model in use is not in memory (nor on its way).
        LoadButton.Visibility = brain is { State: "ready", Model: { } inUse, Loading: null } && !InMemory(brain, inUse) ? Visibility.Visible : Visibility.Collapsed;
        if (ModelBox.IsDropDownOpen) return;
        ModelBox.ItemsSource = options;
        ModelBox.SelectedItem = selected;
    }

    /// <summary>«Para pensar más»: the same model thinking first, any model of the servers, or never.</summary>
    private void FillThinkBox(string chosen, BrainStateMessage? brain)
    {
        var options = new List<ModelOption> { new(string.Empty, Loc.T("brainOptions.thinkSame")) };
        foreach (var model in brain?.Models ?? [])
            options.Add(new ModelOption(model.Name, $"{_app.BrainModelName(model.Name)} · {Describe(model)}".TrimEnd(' ', '·')));
        options.Add(new ModelOption(ModelRefs.None, Loc.T("brainOptions.thinkNone")));
        var selected = chosen.Length == 0 ? options[0]
            : chosen == ModelRefs.None ? options[^1]
            : ModelRefs.Find(options.Skip(1).Select(o => o.Value).Where(v => v != ModelRefs.None), chosen) is { } found ? options.First(o => o.Value == found)
            : null;
        if (selected is null)
        {
            selected = new ModelOption(chosen, Loc.T("brainOptions.modelNotThere", _app.BrainModelName(chosen)));
            options.Insert(1, selected);
        }
        if (ThinkBox.IsDropDownOpen) return;
        ThinkBox.ItemsSource = options;
        ThinkBox.SelectedItem = selected;
    }

    private static bool InMemory(BrainStateMessage? brain, string model) => brain?.Loaded?.Any(l => ModelRefs.Same(l, model)) == true;

    /// <summary>The panel on top: each model in memory with its «Liberar», the one loading, or what is wrong.</summary>
    private void FillState(BrainStateMessage? brain)
    {
        var loaded = brain?.Loaded ?? [];
        var key = $"{_app.Assistant is null}|{brain?.State}|{brain?.Model}|{brain?.Loading}|{brain?.Message}|{string.Join('|', loaded)}|{brain?.Servers?.Count}";
        if (key == _stateShown) return;
        _stateShown = key;
        StateRows.Children.Clear();
        void Add(string brush, string text, string? detail = null, Button? button = null) => StateRows.Children.Add(StateRow(brush, text, detail, button));

        if (_app.Assistant is null) Add("TextFillColorTertiaryBrush", Loc.T("brainOptions.state.moduleOff"));
        else if (brain is null) Add("TextFillColorTertiaryBrush", Loc.T("brainOptions.state.waiting"));
        else
        {
            switch (brain.State)
            {
                case "off": Add("TextFillColorTertiaryBrush", Loc.T("brainOptions.state.off")); break;
                case "checking" when loaded.Count == 0: Add("TextFillColorTertiaryBrush", Loc.T("brainOptions.state.checking")); break;
                case "noModels": Add("Kd.Warning", Loc.T("brainOptions.state.noModels")); break;
                case "modelMissing": Add("Kd.Warning", Loc.T("brainOptions.state.modelMissing", _app.BrainModelName(brain.Model ?? ""))); break;
                case "error": Add("Kd.Danger", Loc.T("brainOptions.state.error"), brain.Message); break;
            }
            // A model of this PC without a llama-server to open it.
            if (brain is { State: "ready", Servers.Count: 0, Model: { } m } && ModelRefs.IsLocal(m)) Add("Kd.Warning", Loc.T("brain.error.noServer"));
            if (brain is { State: "ready", Message: { Length: > 0 } failed }) Add("Kd.Danger", Loc.T("brainOptions.state.loadFailed"), failed);
            if (brain.Loading is { } loading)
                Add("Kd.Warning", Loc.T("brainOptions.state.loading", _app.BrainModelName(loading)), null,
                    Small(SymbolRegular.Dismiss24, Loc.T("common.cancel"), () => _app.AskAssistantBrain("unload", loading)));
            foreach (var model in loaded)
                Add("Kd.Success", Loc.T("brainOptions.state.inMemory", _app.BrainModelName(model)), null,
                    Wide(SymbolRegular.Broom24, Loc.T("brainOptions.freeButton"), () => _app.AskAssistantBrain("unload", model)));
            if (loaded.Count == 0 && brain.Loading is null && brain.State == "ready")
                Add("TextFillColorTertiaryBrush", Loc.T("brainOptions.state.nothingLoaded"));
        }
        Trim(StateRows);
    }

    private Border StateRow(string brush, string text, string? detail, Button? button)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, brush);
        grid.Children.Add(dot);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(body, 1);
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        body.Children.Add(line);
        if (detail is { Length: > 0 }) body.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(body);
        if (button is not null)
        {
            button.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        var row = new Border { Child = grid, Padding = new Thickness(0, 9, 0, 9), BorderThickness = new Thickness(0, 0, 0, 1), MinHeight = 44 };
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(row, text);
        return row;
    }

    // ── every model ──────────────────────────────────────────────────────

    private void FillModels(BrainStateMessage? brain)
    {
        var all = brain?.Models ?? [];
        var local = all.Where(m => ModelRefs.IsLocal(m.Name)).ToList();
        var loaded = brain?.Loaded ?? [];
        ModelsCaption.Text = brain is null ? Loc.T("brainModels.listWaiting")
            : brain.VideoMemoryGb is { } gb ? Loc.T("brainModels.listCount", gb.ToString("0.#", Culture))
            : Loc.T("brainModels.listHint");
        var remotes = _app.Config.Settings.AssistantBrainRemotes;
        var key = string.Join('|', all.Select(m => $"{m.Name}:{m.Vision}:{m.Server}:{InMemory(brain, m.Name)}:{brain?.Loading == m.Name}:{_app.BrainModelArgs(m.Name)}"))
                  + $"|{brain is null}|{_argsOpen}|{_app.Config.Settings.AssistantBrainServers.Count}|"
                  + string.Join('|', remotes.Select(r => $"{r.Id}:{r.Address}:{brain?.Remotes?.FirstOrDefault(x => x.Id == r.Id)?.State}"));
        if (key == _modelsShown) return;
        // Not while its options are being written.
        if (_argsBox is { IsKeyboardFocusWithin: true }) return;
        _modelsShown = key;
        ModelGroups.Children.Clear();
        _argsBox = null;

        var mine = new StackPanel();
        if (brain is not null && local.Count == 0) mine.Children.Add(Note(Loc.T("brainModels.listEmpty")));
        foreach (var model in local) AddModel(mine, model, brain);
        Group(Loc.T("brainModels.groupLocal"), SymbolRegular.Desktop24, mine);

        foreach (var remote in remotes)
        {
            var rows = new StackPanel();
            var state = brain?.Remotes?.FirstOrDefault(x => x.Id == remote.Id);
            var theirs = all.Where(m => m.Remote == remote.Id).ToList();
            if (state is { State: "ok" } && theirs.Count == 0) rows.Children.Add(Note(Loc.T("brainModels.remoteEmpty")));
            else if (state is { State: "unreachable" or "error" }) rows.Children.Add(Note(Loc.T("brainModels.remoteAway")));
            else if (theirs.Count == 0) rows.Children.Add(Note(Loc.T("brainRemotes.checking")));
            foreach (var model in theirs) AddModel(rows, model, brain);
            Group(RemoteName(remote), SymbolRegular.Cloud24, rows);
        }
    }

    /// <summary>A group of models: its name, and its rows in a panel.</summary>
    private void Group(string title, SymbolRegular symbol, StackPanel rows)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, ModelGroups.Children.Count == 0 ? 8 : 16, 0, 0) };
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        header.Children.Add(icon);
        var text = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        header.Children.Add(text);
        ModelGroups.Children.Add(header);
        Trim(rows);
        ModelGroups.Children.Add(new Border { Style = (Style)FindResource("ListPanel"), Child = rows });
    }

    private void AddModel(StackPanel rows, BrainModel model, BrainStateMessage? brain)
    {
        var isLoaded = InMemory(brain, model.Name);
        var isLoading = brain?.Loading is { } loading && ModelRefs.Same(loading, model.Name);
        var local = ModelRefs.IsLocal(model.Name);
        var args = local ? _app.BrainModelArgs(model.Name) : string.Empty;
        var caption = Describe(model)
            + (isLoaded ? " · " + Loc.T("brainModels.loaded") : isLoading ? " · " + Loc.T("brainModels.loading") : "")
            + (args.Length > 0 ? " · " + args : "");
        var buttons = new List<Button>();
        if (isLoaded) buttons.Add(Small(SymbolRegular.Broom24, Loc.T("brainOptions.freeButton"), () => _app.AskAssistantBrain("unload", model.Name)));
        else if (!isLoading) buttons.Add(Small(SymbolRegular.ArrowUpload24, Loc.T("brainModels.load"), () => _app.AskAssistantBrain("load", model.Name)));
        if (local)
            buttons.Add(Small(SymbolRegular.Options24, Loc.T("brainModels.options"), () =>
            {
                _argsOpen = _argsOpen == model.Name ? null : model.Name;
                FillModels(_app.AssistantBrain);
                _argsBox?.Focus();
            }));
        if (_app.IsDownloadedModel(model.Name))
            buttons.Add(Small(SymbolRegular.Delete24, Loc.T("brainModels.delete"), () => DeleteModel(model.Name)));
        var name = local ? BrainProviders.ModelLabel(model.Name) : ModelRefs.Label(model.Name);
        var row = Row(isLoaded ? SymbolRegular.BrainCircuit24 : SymbolRegular.Cube24, name, caption,
            isLoaded ? "Kd.Success" : isLoading ? "Kd.Warning" : "Kd.AccentText", buttons);
        row.ToolTip = local ? model.Name : _app.BrainModelName(model.Name);
        rows.Children.Add(row);
        if (_argsOpen == model.Name) rows.Children.Add(ArgsEditor(model.Name, args));
    }

    /// <summary>A model's own llama-server options, under its row: saved when the box is left or with Intro.</summary>
    private Border ArgsEditor(string model, string args)
    {
        var box = new TextBox { Text = args, PlaceholderText = "-c 32768", Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(box, Loc.T("brainModels.options"));
        void Save()
        {
            _app.SetBrainModelArgs(model, box.Text);
        }
        box.LostKeyboardFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            if (e.Key == Key.Escape) box.Text = _app.BrainModelArgs(model);
            Save();
            _argsOpen = null;
            _argsBox = null;
            FillModels(_app.AssistantBrain);
            e.Handled = true;
        };
        _argsBox = box;
        var panel = new StackPanel { Margin = new Thickness(30, 2, 0, 10) };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { Text = Loc.T("brainModels.optionsHint"), Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap });
        var row = new Border { Child = panel, BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return row;
    }

    private async void DeleteModel(string path)
    {
        var name = BrainProviders.ModelLabel(path);
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("brainModels.deleteTitle"),
            Content = Loc.T("brainModels.deleteText", name),
            PrimaryButtonText = Loc.T("brainModels.delete"),
            CloseButtonText = Loc.T("common.cancel"),
            Width = 480,
            MaxWidth = 480,
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary) await _app.DeleteDownloadedModelAsync(path);
    }

    private void FillDownload()
    {
        var download = _app.AssistantModelDownload;
        var moving = _app.MovingModelsFolder;
        AddModelButton.IsEnabled = download is not { Running: true } && !moving;
        if (moving)
        {
            DownloadBox.Visibility = Visibility.Visible;
            DownloadText.Text = Loc.T("brainModels.moving");
            DownloadProgress.IsIndeterminate = true;
            DownloadProgress.Visibility = Visibility.Visible;
            CancelDownloadButton.Visibility = Visibility.Collapsed;
            return;
        }
        if (download is null || (!download.Running && download.Error is null))
        {
            DownloadBox.Visibility = Visibility.Collapsed;
            return;
        }
        DownloadBox.Visibility = Visibility.Visible;
        CancelDownloadButton.Visibility = download.Running ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Visibility = download.Running ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.IsIndeterminate = download.Total <= 0;
        DownloadProgress.Value = download.Total > 0 ? (double)download.Done / download.Total : 0;
        DownloadText.Text = download.Running
            ? Loc.T("brainModels.downloading", download.Name, Gb(download.Done), Gb(download.Total), download.Total > 0 ? (100.0 * download.Done / download.Total).ToString("0", Culture) : "0")
            : Loc.T("brainModels.downloadFailed", download.Name, download.Error ?? "");
    }

    // ── llama-servers and folders ────────────────────────────────────────

    /// <summary>«Llama-CPP», «Bonsai-demo»: the folder that tells a server apart.</summary>
    public static string ServerLabel(string server)
    {
        var folder = System.IO.Path.GetDirectoryName(server);
        while (folder is not null && System.IO.Path.GetFileName(folder).ToLowerInvariant() is "bin" or "cuda" or "cpu" or "vulkan" or "build" or "release" or "x64")
            folder = System.IO.Path.GetDirectoryName(folder);
        return folder is null ? "llama-server" : System.IO.Path.GetFileName(folder);
    }

    private void FillServers(BrainStateMessage? brain)
    {
        var servers = _app.Config.Settings.AssistantBrainServers;
        var key = string.Join('|', servers.Select(s => s + ":" + brain?.Servers?.FirstOrDefault(x => string.Equals(x.Path, s, StringComparison.OrdinalIgnoreCase))));
        if (key == _serversShown) return;
        _serversShown = key;
        ServerRows.Children.Clear();
        if (servers.Count == 0) ServerRows.Children.Add(Note(Loc.T("brainModels.noServers")));
        for (var i = 0; i < servers.Count; i++)
        {
            var path = servers[i];
            var state = brain?.Servers?.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            var caption = state is { Exists: false } ? Loc.T("brainModels.serverMissing")
                : (i == 0 ? Loc.T("brainModels.serverUsual") : Loc.T("brainModels.serverOther"))
                  + (state is null ? "" : " · " + Loc.T("brainModels.serverModels", state.Models));
            var buttons = new List<Button>();
            if (i > 0) buttons.Add(Small(SymbolRegular.ArrowUp24, Loc.T("brainModels.makeUsual"), () => _app.MakeBrainServerFirst(path)));
            buttons.Add(Small(SymbolRegular.Delete24, Loc.T("brainModels.remove"), () => _app.RemoveBrainServer(path)));
            var row = Row(SymbolRegular.Server24, ServerLabel(path), caption, state is { Exists: false } ? "Kd.Danger" : "Kd.AccentText", buttons);
            row.ToolTip = path;
            ServerRows.Children.Add(row);
        }
        Trim(ServerRows);
    }

    private void FillFolders()
    {
        var folders = _app.Config.Settings.AssistantBrainFolders;
        var busy = _app.AssistantModelDownload is { Running: true } || _app.MovingModelsFolder;
        var key = _app.AssistantModelsFolder + "|" + busy + "|" + string.Join('|', folders);
        if (key == _foldersShown) return;
        _foldersShown = key;
        FolderRows.Children.Clear();
        var move = Small(SymbolRegular.FolderArrowRight24, Loc.T("brainModels.moveFolderTitle"), OnMoveDownloads);
        move.IsEnabled = !busy;
        FolderRows.Children.Add(Row(SymbolRegular.ArrowDownload24, _app.AssistantModelsFolder, Loc.T("brainModels.downloadsFolder"), "Kd.AccentText",
            [Small(SymbolRegular.FolderOpen24, Loc.T("brainModels.openFolder"), OnOpenDownloads), move]));
        foreach (var folder in folders)
        {
            var exists = System.IO.Directory.Exists(folder);
            FolderRows.Children.Add(Row(SymbolRegular.Folder24, folder, exists ? string.Empty : Loc.T("brainModels.folderMissing"), exists ? "Kd.AccentText" : "Kd.Danger",
                [Small(SymbolRegular.Delete24, Loc.T("brainModels.remove"), () => _app.RemoveBrainFolder(folder))]));
        }
        Trim(FolderRows);
    }

    // ── other servers ────────────────────────────────────────────────────

    /// <summary>«Ollama · localhost:11434».</summary>
    private static string RemoteName(BrainRemote remote) =>
        $"{KindName(remote.Kind)} · {(Uri.TryCreate(remote.Address, UriKind.Absolute, out var uri) ? uri.Authority : remote.Address)}";

    private void FillRemotes(BrainStateMessage? brain)
    {
        var remotes = _app.Config.Settings.AssistantBrainRemotes;
        var key = string.Join('|', remotes.Select(r => $"{r.Id}:{r.Kind}:{r.Address}:{brain?.Remotes?.FirstOrDefault(x => x.Id == r.Id)}"));
        if (key == _remotesShown) return;
        _remotesShown = key;
        RemoteRows.Children.Clear();
        if (remotes.Count == 0) RemoteRows.Children.Add(Note(Loc.T("brainRemotes.none")));
        foreach (var remote in remotes)
        {
            var state = brain?.Remotes?.FirstOrDefault(x => x.Id == remote.Id);
            var (caption, bad) = state switch
            {
                null or { State: "checking" } => (Loc.T("brainRemotes.checking"), false),
                { State: "ok" } => (Loc.T("brainRemotes.ok", state.Models, state.Ms), false),
                { State: "unreachable" } => (Loc.T("brainRemotes.unreachable") + (state.Message is { Length: > 0 } m ? " · " + m : ""), true),
                _ => (Loc.T("brainRemotes.error", state.Message ?? ""), true),
            };
            caption += " · " + Place(remote.Address);
            var id = remote.Id;
            RemoteRows.Children.Add(Row(SymbolRegular.Cloud24, RemoteName(remote), caption, bad ? "Kd.Danger" : "Kd.AccentText",
            [
                Small(SymbolRegular.PlugConnected24, Loc.T("brainRemotes.test"), () => _app.AskAssistantBrain("test", remote: id)),
                Small(SymbolRegular.Delete24, Loc.T("brainModels.remove"), () => _app.RemoveBrainRemote(id)),
            ]));
        }
        Trim(RemoteRows);
    }

    /// <summary>Whether what is said to the assistant leaves the PC.</summary>
    private static string Place(string address)
    {
        var host = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host : address;
        return BrainProviders.PlaceOf(address) switch
        {
            EndpointPlace.ThisPc => Loc.T("brainOptions.place.thisPc"),
            EndpointPlace.LocalNetwork => Loc.T("brainOptions.place.network", host),
            EndpointPlace.Internet => Loc.T("brainOptions.place.internet", host),
            _ => Loc.T("brainOptions.place.invalid"),
        };
    }

    /// <summary>The address being typed, as it would be kept (the usual one of its kind when empty).</summary>
    private string NewAddress(RemoteKind kind) =>
        NewEndpointBox.Text.Trim() is { Length: > 0 } typed ? (typed.Contains("://", StringComparison.Ordinal) ? typed : "http://" + typed) : kind.DefaultEndpoint();

    private void FillNewRemote()
    {
        if (NewKindBox.SelectedItem is not KindOption kind) return;
        NewEndpointBox.PlaceholderText = kind.Kind.DefaultEndpoint();
        NewKeyBox.Visibility = kind.Kind.UsesApiKey() ? Visibility.Visible : Visibility.Hidden;
        var address = NewAddress(kind.Kind);
        NewPlaceText.Text = _app.Config.Settings.AssistantBrainRemotes.Any(r => BrainProviders.SameAddress(r.Address, address))
            ? Loc.T("brainRemotes.duplicate")
            : Place(address);
    }

    // ── rows ─────────────────────────────────────────────────────────────

    private Border Row(SymbolRegular symbol, string title, string caption, string iconBrush, IReadOnlyList<Button> buttons)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 18, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(ForegroundProperty, iconBrush);
        grid.Children.Add(icon);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(body, 1);
        var text = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        body.Children.Add(text);
        if (caption.Length > 0)
        {
            var under = new TextBlock { Text = caption, Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap };
            if (iconBrush == "Kd.Danger") under.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
            body.Children.Add(under);
        }
        grid.Children.Add(body);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(panel, 2);
        foreach (var button in buttons) panel.Children.Add(button);
        grid.Children.Add(panel);
        var row = new Border { Child = grid, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(row, title);
        return row;
    }

    /// <summary>The last row of a panel without its line underneath.</summary>
    private static void Trim(StackPanel rows)
    {
        foreach (var row in rows.Children.OfType<Border>()) row.BorderThickness = new Thickness(0, 0, 0, 1);
        if (rows.Children.Count > 0 && rows.Children[^1] is Border last) last.BorderThickness = new Thickness(0);
    }

    private TextBlock Note(string text) => new() { Text = text, Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 10, 0, 10), TextWrapping = TextWrapping.Wrap };

    private static Button Small(SymbolRegular symbol, string name, Action click)
    {
        var button = new Button
        {
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A button with its text (the «Liberar» of what is in memory).</summary>
    private static Button Wide(SymbolRegular symbol, string text, Action click)
    {
        var button = new Button { Icon = new SymbolIcon { Symbol = symbol }, Content = text };
        button.Click += (_, _) => click();
        return button;
    }

    // ── changes ──────────────────────────────────────────────────────────

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ModelBox.SelectedItem is not ModelOption option || option.Value == _app.Config.Settings.AssistantBrainModel) return;
        _app.Config.Settings.AssistantBrainModel = option.Value;
    }

    private void OnThinkChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ThinkBox.SelectedItem is not ModelOption option || option.Value == _app.Config.Settings.AssistantBrainThinkModel) return;
        _app.Config.Settings.AssistantBrainThinkModel = option.Value;
        ThinkTokensRow.Visibility = option.Value == ModelRefs.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnThinkTokensChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ThinkTokensBox.SelectedItem is not ThinkOption option) return;
        _app.Config.Settings.AssistantBrainThinkTokens = option.Tokens;
    }

    private void OnCheck(object sender, RoutedEventArgs e) => _app.AskAssistantBrain("rescan");

    private void OnLoad(object sender, RoutedEventArgs e) => _app.AskAssistantBrain("load");

    private void OnOneAtATime(object sender, RoutedEventArgs e)
    {
        if (!_filling) _app.Config.Settings.AssistantBrainOneAtATime = OneAtATimeSwitch.IsChecked == true;
    }

    private void OnVision(object sender, RoutedEventArgs e)
    {
        if (!_filling) _app.Config.Settings.AssistantBrainVision = VisionSwitch.IsChecked == true;
    }

    private void OnKeepChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || KeepBox.SelectedItem is not KeepOption option) return;
        _app.Config.Settings.AssistantBrainKeepMinutes = option.Minutes;
    }

    private void OnAddServer(object sender, RoutedEventArgs e)
    {
        var near = _app.Config.Settings.AssistantBrainServers.LastOrDefault() ?? string.Empty;
        if (Browse(Loc.T("brainOptions.browseServerTitle"), "llama-server.exe|llama-server.exe|*.exe|*.exe", near) is { } path) _app.AddBrainServer(path);
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("brainModels.addFolderTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _app.AddBrainFolder(dialog.FolderName);
    }

    /// <summary>A .gguf chosen by hand becomes the general model (and is listed even outside the folders).</summary>
    private void OnBrowseModel(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        var near = ModelRefs.IsLocal(s.AssistantBrainModel) ? s.AssistantBrainModel : s.AssistantBrainFolders.FirstOrDefault() ?? _app.AssistantModelsFolder + "\\";
        if (Browse(Loc.T("brainOptions.browseModelTitle"), "*.gguf|*.gguf", near) is { } path) s.AssistantBrainModel = path;
    }

    private void OnAddModel(object sender, RoutedEventArgs e) => new AddModelWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void OnCancelDownload(object sender, RoutedEventArgs e) => _app.CancelModelDownload();

    private void OnOpenDownloads()
    {
        var folder = _app.AssistantModelsFolder;
        System.IO.Directory.CreateDirectory(folder);
        Core.Platform.ProcessLauncher.Launch(new Core.Platform.LaunchSpec("explorer.exe", $"\"{folder}\""), asAdmin: false);
    }

    private async void OnMoveDownloads()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("brainModels.moveFolderTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await _app.MoveModelsFolderAsync(dialog.FolderName);
    }

    private void OnNewKindChanged(object sender, SelectionChangedEventArgs e) => FillNewRemote();

    private void OnNewEndpointChanged(object sender, TextChangedEventArgs e) => FillNewRemote();

    private void OnAddRemote(object sender, RoutedEventArgs e)
    {
        if (NewKindBox.SelectedItem is not KindOption kind) return;
        var typed = NewEndpointBox.Text.Trim();
        if (BrainProviders.PlaceOf(NewAddress(kind.Kind)) == EndpointPlace.Invalid)
        {
            NewPlaceText.Text = Loc.T("brainOptions.place.invalid");
            return;
        }
        if (_app.AddBrainRemote(kind.Kind, typed, kind.Kind.UsesApiKey() ? NewKeyBox.Password : string.Empty) is not { } id)
        {
            NewPlaceText.Text = Loc.T("brainRemotes.duplicate");
            return;
        }
        NewEndpointBox.Text = string.Empty;
        NewKeyBox.Password = string.Empty;
        // Once the process has the new list, it is tried at once.
        Dispatcher.BeginInvoke(() => _app.AskAssistantBrain("test", remote: id), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>A file chosen in the usual dialog, starting next to <paramref name="near"/>; null when cancelled.</summary>
    private string? Browse(string title, string filter, string near)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        try
        {
            if (near.Length > 0 && System.IO.Path.GetDirectoryName(near) is { } folder && System.IO.Directory.Exists(folder)) dialog.InitialDirectory = folder;
        }
        catch (ArgumentException)
        {
            // A path typed by hand that is not one.
        }
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private void OnGoGeneral(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistant");
}
