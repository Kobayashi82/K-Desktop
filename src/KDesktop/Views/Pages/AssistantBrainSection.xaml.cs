using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Views.Assistant;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Brain (stage 3.1; its own brain since 5.0.107): the general model, how it is now, one model at a
/// time, vision, how long a model stays, freeing the graphics card; the llama-servers and folders of this PC with the
/// models found, those of Hugging Face (add, download, delete, take the folder elsewhere); and the other servers (Ollama,
/// llama-swap, OpenAI compatible) with «Probar». The work is the assistant process's (downloads are K-Desktop's): this
/// sends <c>brain</c> requests and shows its <c>brainState</c>. While shown, it asks again every few seconds.
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

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _filling;
    /// <summary>What each list of rows shows, so it is made again only when that changes (a button under the mouse stays).</summary>
    private string? _serversShown, _foldersShown, _modelsShown, _remotesShown;

    public AssistantBrainSection()
    {
        InitializeComponent();
        KeepBox.ItemsSource = KeepChoices.Select(m => new KeepOption(m, m switch
        {
            0 => Loc.T("brainOptions.keepNever"),
            60 => Loc.T("brainOptions.keepHour"),
            _ => Loc.T("brainOptions.keepMinutes", m),
        })).ToList();
        NewKindBox.ItemsSource = new[] { RemoteKind.Ollama, RemoteKind.LlamaSwap, RemoteKind.OpenAI }.Select(k => new KindOption(k, KindName(k))).ToList();
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
            Body.IsEnabled = LocalCard.IsEnabled = RemoteCard.IsEnabled = s.AssistantEnabled;

            OneAtATimeSwitch.IsChecked = s.AssistantBrainOneAtATime;
            VisionSwitch.IsChecked = s.AssistantBrainVision;
            KeepBox.SelectedItem = KeepBox.Items.OfType<KeepOption>().FirstOrDefault(k => k.Minutes == s.AssistantBrainKeepMinutes)
                ?? KeepBox.Items.OfType<KeepOption>().First(k => k.Minutes == BrainConfig.DefaultKeepMinutes);
            if (!ServerArgsBox.IsKeyboardFocusWithin) ServerArgsBox.Text = s.AssistantBrainServerArgs;

            FillModelBox(s.AssistantBrainModel, brain);
            FillState(brain);
            FillFree(brain);
            FillServers(brain);
            FillFolders();
            FillModels(brain);
            FillDownload();
            FillRemotes(brain);
            FillNewRemote();
        }
        finally
        {
            _filling = false;
        }
    }

    // ── the general model and how it is ──────────────────────────────────

    /// <summary>What a model is, in a few words: its size, whether it sees, and the llama-server or server it is in.</summary>
    private string Describe(BrainModel model)
    {
        var parts = new List<string>();
        if (model.SizeBytes > 0) parts.Add(Gb(model.SizeBytes) + " GB");
        else if (model.Billions > 0) parts.Add(Loc.T("brainOptions.billions", model.Billions.ToString("0.#", Culture)));
        if (model.Vision) parts.Add(Loc.T("brainModels.vision"));
        if (!model.Tools) parts.Add(Loc.T("brainOptions.noTools"));
        var servers = _app.Config.Settings.AssistantBrainServers;
        if (model.Server is { } server && servers.Count > 1) parts.Add(ServerLabel(server));
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
        if (ModelBox.IsDropDownOpen) return;
        ModelBox.ItemsSource = options;
        ModelBox.SelectedItem = selected;
    }

    private void FillState(BrainStateMessage? brain)
    {
        var (text, detail, brush) = (_app.Assistant, brain) switch
        {
            (null, _) => (Loc.T("brainOptions.state.moduleOff"), null, "TextFillColorTertiaryBrush"),
            (_, null) => (Loc.T("brainOptions.state.waiting"), null, "TextFillColorTertiaryBrush"),
            (_, { State: "off" }) => (Loc.T("brainOptions.state.off"), null, "TextFillColorTertiaryBrush"),
            (_, { State: "checking" }) => (Loc.T("brainOptions.state.checking"), null, "TextFillColorTertiaryBrush"),
            (_, { State: "ready", Loading: { } loading }) => (Loc.T("brainOptions.state.loading", _app.BrainModelName(loading)), null, "Kd.Warning"),
            (_, { State: "ready", Model: { } model } b) => (b.Loaded?.Any(l => ModelRefs.Same(l, model)) == true
                ? Loc.T("brainOptions.state.readyLoaded", _app.BrainModelName(model)) : Loc.T("brainOptions.state.ready", _app.BrainModelName(model)), b.Message, "Kd.Success"),
            (_, { State: "noModels" }) => (Loc.T("brainOptions.state.noModels"), null, "Kd.Warning"),
            (_, { State: "modelMissing" } b) => (Loc.T("brainOptions.state.modelMissing", _app.BrainModelName(b.Model ?? "")), null, "Kd.Warning"),
            (_, { } b) => (Loc.T("brainOptions.state.error"), b.Message, "Kd.Danger"),
        };
        // A model that is not here because its llama-server is missing says so.
        if (brain is { State: "ready", Servers: { } servers } && servers.Count == 0 && brain.Model is { } m && ModelRefs.IsLocal(m))
            (detail, brush) = (Loc.T("brain.error.noServer"), "Kd.Warning");
        StateText.Text = text;
        StateDetail.Text = detail ?? string.Empty;
        StateDetail.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        StateDot.SetResourceReference(Shape.FillProperty, brush);
        CheckButton.IsEnabled = brain is not null;
    }

    private void FillFree(BrainStateMessage? brain)
    {
        var loaded = brain?.Loaded ?? [];
        var loading = brain?.Loading is not null;
        FreeText.Text = loaded.Count > 0 ? Loc.T("brainOptions.freeLoaded", string.Join(", ", loaded.Select(_app.BrainModelName))) : Loc.T("brainOptions.freeHint");
        FreeButton.IsEnabled = loaded.Count > 0 || loading;
        LoadButton.IsEnabled = brain is { State: "ready", Model: { } model } && !loading && !loaded.Any(l => ModelRefs.Same(l, model));
    }

    // ── llama-servers and folders ────────────────────────────────────────

    /// <summary>«llama.cpp (Llama-CPP)», «Bonsai-demo»: the folder that tells a server apart.</summary>
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
            ServerRows.Children.Add(Row(SymbolRegular.Server24, $"{ServerLabel(path)}  —  {path}", caption, state is { Exists: false }, buttons));
        }
    }

    private void FillFolders()
    {
        var folders = _app.Config.Settings.AssistantBrainFolders;
        var key = _app.AssistantModelsFolder + "|" + string.Join('|', folders);
        if (key == _foldersShown) return;
        _foldersShown = key;
        FolderRows.Children.Clear();
        FolderRows.Children.Add(Row(SymbolRegular.ArrowDownload24, _app.AssistantModelsFolder, Loc.T("brainModels.downloadsFolder"), false, []));
        foreach (var folder in folders)
        {
            var exists = System.IO.Directory.Exists(folder);
            FolderRows.Children.Add(Row(SymbolRegular.Folder24, folder, exists ? string.Empty : Loc.T("brainModels.folderMissing"), !exists,
                [Small(SymbolRegular.Delete24, Loc.T("brainModels.remove"), () => _app.RemoveBrainFolder(folder))]));
        }
        DownloadsFolderText.Text = _app.AssistantModelsFolder;
    }

    // ── the models of this PC ────────────────────────────────────────────

    private void FillModels(BrainStateMessage? brain)
    {
        var local = (brain?.Models ?? []).Where(m => ModelRefs.IsLocal(m.Name)).ToList();
        var loaded = brain?.Loaded ?? [];
        ModelsCaption.Text = brain is null ? Loc.T("brainModels.listWaiting")
            : local.Count == 0 ? Loc.T("brainModels.listEmpty")
            : Loc.T("brainModels.listCount", local.Count, brain.VideoMemoryGb is { } gb ? gb.ToString("0.#", Culture) : "?");
        var key = string.Join('|', local.Select(m => $"{m.Name}:{m.Vision}:{m.Server}:{loaded.Any(l => ModelRefs.Same(l, m.Name))}:{brain?.Loading == m.Name}"))
                  + _app.Config.Settings.AssistantBrainServers.Count;
        if (key == _modelsShown) return;
        _modelsShown = key;
        ModelRows.Children.Clear();
        foreach (var model in local)
        {
            var isLoaded = loaded.Any(l => ModelRefs.Same(l, model.Name));
            var caption = Describe(model) + (isLoaded ? " · " + Loc.T("brainModels.loaded") : brain?.Loading == model.Name ? " · " + Loc.T("brainModels.loading") : "");
            var buttons = new List<Button>();
            if (!isLoaded) buttons.Add(Small(SymbolRegular.ArrowUpload24, Loc.T("brainModels.load"), () => _app.AskAssistantBrain("load", model.Name)));
            if (_app.IsDownloadedModel(model.Name))
                buttons.Add(Small(SymbolRegular.Delete24, Loc.T("brainModels.delete"), () => DeleteModel(model.Name)));
            var row = Row(isLoaded ? SymbolRegular.BrainCircuit24 : SymbolRegular.Cube24, BrainProviders.ModelLabel(model.Name), caption, false, buttons);
            row.ToolTip = model.Name;
            ModelRows.Children.Add(row);
        }
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
        MoveFolderButton.IsEnabled = download is not { Running: true } && !moving;
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

    // ── other servers ────────────────────────────────────────────────────

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
            RemoteRows.Children.Add(Row(SymbolRegular.Cloud24, $"{KindName(remote.Kind)}  —  {remote.Address}", caption, bad,
            [
                Small(SymbolRegular.PlugConnected24, Loc.T("brainRemotes.test"), () => _app.AskAssistantBrain("test", remote: id)),
                Small(SymbolRegular.Delete24, Loc.T("brainModels.remove"), () => _app.RemoveBrainRemote(id)),
            ]));
        }
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

    private void FillNewRemote()
    {
        if (NewKindBox.SelectedItem is not KindOption kind) return;
        NewEndpointBox.PlaceholderText = kind.Kind.DefaultEndpoint();
        NewKeyBox.Visibility = kind.Kind.UsesApiKey() ? Visibility.Visible : Visibility.Hidden;
        var address = NewEndpointBox.Text.Trim() is { Length: > 0 } typed ? (typed.Contains("://", StringComparison.Ordinal) ? typed : "http://" + typed) : kind.Kind.DefaultEndpoint();
        NewPlaceText.Text = Place(address);
    }

    // ── rows ─────────────────────────────────────────────────────────────

    private Border Row(SymbolRegular symbol, string title, string caption, bool bad, IReadOnlyList<Button> buttons)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 18, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(ForegroundProperty, bad ? "Kd.Danger" : "Kd.AccentText");
        grid.Children.Add(icon);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(body, 1);
        var text = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        body.Children.Add(text);
        if (caption.Length > 0)
        {
            var under = new TextBlock { Text = caption, Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap };
            if (bad) under.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
            body.Children.Add(under);
        }
        grid.Children.Add(body);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(panel, 2);
        foreach (var button in buttons) panel.Children.Add(button);
        grid.Children.Add(panel);
        var row = new Border { Child = grid, Padding = new Thickness(0, 6, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "Kd.Subtle");
        AutomationProperties.SetName(row, title);
        return row;
    }

    private TextBlock Note(string text) => new() { Text = text, Style = (Style)FindResource("Caption"), Margin = new Thickness(30, 4, 0, 4), TextWrapping = TextWrapping.Wrap };

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

    // ── changes ──────────────────────────────────────────────────────────

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ModelBox.SelectedItem is not ModelOption option || option.Value == _app.Config.Settings.AssistantBrainModel) return;
        _app.Config.Settings.AssistantBrainModel = option.Value;
    }

    private void OnCheck(object sender, RoutedEventArgs e) => _app.AskAssistantBrain("rescan");

    private void OnFree(object sender, RoutedEventArgs e) => _app.AskAssistantBrain("unload");

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

    private void OnServerArgsLeft(object sender, RoutedEventArgs e)
    {
        var typed = ServerArgsBox.Text.Trim();
        if (typed != _app.Config.Settings.AssistantBrainServerArgs) _app.Config.Settings.AssistantBrainServerArgs = typed;
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

    private void OnOpenDownloads(object sender, RoutedEventArgs e)
    {
        var folder = _app.AssistantModelsFolder;
        System.IO.Directory.CreateDirectory(folder);
        Core.Platform.ProcessLauncher.Launch(new Core.Platform.LaunchSpec("explorer.exe", $"\"{folder}\""), asAdmin: false);
    }

    private async void OnMoveDownloads(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("brainModels.moveFolderTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await _app.MoveModelsFolderAsync(dialog.FolderName);
    }

    private void OnNewKindChanged(object sender, SelectionChangedEventArgs e) => FillNewRemote();

    private void OnAddRemote(object sender, RoutedEventArgs e)
    {
        if (NewKindBox.SelectedItem is not KindOption kind) return;
        var typed = NewEndpointBox.Text.Trim();
        var address = typed.Length == 0 ? kind.Kind.DefaultEndpoint() : typed.Contains("://", StringComparison.Ordinal) ? typed : "http://" + typed;
        if (BrainProviders.PlaceOf(address) == EndpointPlace.Invalid)
        {
            NewPlaceText.Text = Loc.T("brainOptions.place.invalid");
            return;
        }
        var id = _app.AddBrainRemote(kind.Kind, typed, kind.Kind.UsesApiKey() ? NewKeyBox.Password : string.Empty);
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
