using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → «Lo que sabe» (stage 19.4): the folders of the user's documents the assistants know (add, open,
/// see what was read of each one, take out), how far the reading goes («Leyendo 412 de 1.300 archivos») and why it waits,
/// reading pictures with the OCR, reading it all again and forgetting it; and a search to try, with what an assistant would
/// get. The rows are made in code (an ItemsControl hides its buttons from UI Automation).
/// </summary>
public partial class AssistantKnowledgeSection : UserControl
{
    private readonly App _app = App.Current;
    /// <summary>The folder whose files are shown (null: none).</summary>
    private string? _showing;
    private DateTime _filesAsked;

    public AssistantKnowledgeSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantKnowledgeChanged += Fill;
            _app.AssistantKnowledgeFiles += ShowFiles;
            _app.AssistantKnowledgeFound += ShowFound;
            _app.AssistantChanged += OnAssistantChanged;
            _app.Config.Settings.PropertyChanged += OnSettingChanged;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantKnowledgeChanged -= Fill;
            _app.AssistantKnowledgeFiles -= ShowFiles;
            _app.AssistantKnowledgeFound -= ShowFound;
            _app.AssistantChanged -= OnAssistantChanged;
            _app.Config.Settings.PropertyChanged -= OnSettingChanged;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _app.RequestAssistantKnowledge();
            Fill();
        };
    }

    private void OnAssistantChanged()
    {
        if (!IsVisible) return;
        if (_app.AssistantKnowledge is null) _app.RequestAssistantKnowledge();
        Fill();
    }

    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Core.Model.AppSettings.AssistantKnowledgeFolders) && IsVisible) Fill();
    }

    private void Fill()
    {
        var running = _app.Assistant is { IsRunning: true };
        OffBox.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        OffText.Text = Loc.T(_app.Assistant is null ? "brainOptions.moduleOff" : "knowledgeOptions.notRunning");
        GoGeneral.Visibility = _app.Assistant is null ? Visibility.Visible : Visibility.Collapsed;

        var state = _app.AssistantKnowledge;
        var folders = _app.Config.Settings.AssistantKnowledgeFolders;
        FillState(state, folders.Count, running);
        FolderRows.Children.Clear();
        foreach (var folder in folders)
            FolderRows.Children.Add(FolderRow(folder, state?.Folders.FirstOrDefault(f => string.Equals(f.Path, folder, StringComparison.OrdinalIgnoreCase)), running));
        FolderPanel.Visibility = folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Actions.Visibility = folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildButton.IsEnabled = running;
        TryCard.Visibility = folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.IsEnabled = running;
        if (_showing is not null && !folders.Contains(_showing, StringComparer.OrdinalIgnoreCase)) CloseFiles();
        // The files shown follow the reading, every few seconds (a thousand rows each time).
        else if (_showing is not null && state is { Phase: "indexing" or "scanning" } && DateTime.UtcNow - _filesAsked > TimeSpan.FromSeconds(3))
            AskFiles(_showing);
    }

    private void FillState(KnowledgeStateMessage? state, int folders, bool running)
    {
        var culture = Loc.Instance.Culture;
        string Count(int n) => n.ToString("N0", culture);
        StateBox.Visibility = folders > 0 && running ? Visibility.Visible : Visibility.Collapsed;
        WordsOnlyBox.Visibility = state is { WordsOnly: true } && folders > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (state is null || folders == 0)
        {
            StateText.Text = Loc.T("knowledgeOptions.state.loading");
            DetailText.Text = string.Empty;
            Progress.Visibility = Visibility.Collapsed;
            return;
        }
        var reading = state.Phase is "indexing" or "paused";
        StateText.Text = state.Phase switch
        {
            "indexing" => Loc.T("knowledgeOptions.state.indexing", Count(state.Done), Count(state.Files)),
            "paused" => Loc.T("knowledgeOptions.state.paused", Count(state.Done), Count(state.Files)),
            "scanning" => Loc.T("knowledgeOptions.state.scanning"),
            "loading" => Loc.T("knowledgeOptions.state.loading"),
            _ => Loc.T("knowledgeOptions.state.ready", Count(state.Files), Count(state.Pieces)),
        };
        StateIcon.Symbol = state.Phase switch
        {
            "paused" => SymbolRegular.PauseCircle24,
            "ready" => SymbolRegular.CheckmarkCircle24,
            _ => SymbolRegular.BookOpen24,
        };
        DetailText.Text = state.Phase == "paused" && state.Paused is { } why ? Loc.T("knowledgeOptions.paused." + why)
            : state.Current is { } current ? Loc.T("knowledgeOptions.current", Path.GetFileName(current))
            : Loc.T("knowledgeOptions.size", Megabytes(state.Bytes));
        Progress.Visibility = reading && state.Files > 0 ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = state.Files > 0 ? state.Done / (double)state.Files : 0;
    }

    private static string Megabytes(long bytes) => string.Create(Loc.Instance.Culture, $"{bytes / 1048576.0:0.#} MB");

    private Border FolderRow(string folder, KnowledgeFolderInfo? info, bool running)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = info is { Online: false } ? SymbolRegular.FolderProhibited24 : SymbolRegular.Folder24, FontSize = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        grid.Children.Add(icon);

        var culture = Loc.Instance.Culture;
        var name = new TextBlock { Text = folder, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14, ToolTip = folder };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var parts = new List<string>();
        if (info is null) parts.Add(Loc.T(running ? "knowledgeOptions.folder.looking" : "knowledgeOptions.folder.later"));
        else if (!info.Online) parts.Add(Loc.T("knowledgeOptions.folder.offline"));
        else
        {
            parts.Add(info.Done < info.Files
                ? Loc.T("knowledgeOptions.folder.reading", info.Done.ToString("N0", culture), info.Files.ToString("N0", culture))
                : Loc.T("knowledgeOptions.folder.files", info.Files.ToString("N0", culture)));
            parts.Add(Loc.T("knowledgeOptions.folder.pieces", info.Pieces.ToString("N0", culture)));
            if (info.Failed > 0) parts.Add(Loc.T("knowledgeOptions.folder.failed", info.Failed.ToString("N0", culture)));
        }
        var caption = new TextBlock { Style = (Style)FindResource("Caption"), Text = string.Join(" · ", parts) };
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(name);
        body.Children.Add(caption);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(buttons, 2);
        buttons.Children.Add(Small(SymbolRegular.DocumentBulletList24, Loc.T("knowledgeOptions.files"), folder, () => AskFiles(folder), running && info is not null));
        buttons.Children.Add(Small(SymbolRegular.FolderOpen24, Loc.T("knowledgeOptions.open"), folder, () => Open(folder), Directory.Exists(folder)));
        buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("knowledgeOptions.remove"), folder, () => _app.RemoveKnowledgeFolder(folder), true));
        grid.Children.Add(buttons);

        var row = new Border { Child = grid, Padding = new Thickness(0, 9, 0, 9), BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(row, folder);
        return row;
    }

    private static Button Small(SymbolRegular symbol, string name, string target, Action click, bool enabled)
    {
        var button = new Button
        {
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = name,
            IsEnabled = enabled,
        };
        AutomationProperties.SetName(button, name + " " + Path.GetFileName(target));
        button.Click += (_, _) => click();
        return button;
    }

    private static void Open(string folder) => ProcessLauncher.Launch(new LaunchSpec("explorer.exe", $"\"{folder}\""), asAdmin: false);

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("knowledgeOptions.addTitle"), Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var problems = dialog.FolderNames.Select(f => (Folder: f, Problem: _app.AddKnowledgeFolder(f))).Where(p => p.Problem is not null).ToList();
        ProblemText.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemText.Text = string.Join("\n", problems.Select(p => $"{p.Folder}: {Loc.T("knowledgeOptions.problem." + p.Problem)}"));
        Fill();
    }

    private void OnRebuild(object sender, RoutedEventArgs e)
    {
        _app.RebuildAssistantKnowledge();
        CloseFiles();
    }

    private async void OnForget(object sender, RoutedEventArgs e)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("knowledgeOptions.forgetTitle"),
            Width = 480,
            MaxWidth = 480,
            Content = Loc.T("knowledgeOptions.forgetQuestion"),
            PrimaryButtonText = Loc.T("knowledgeOptions.forget"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        _app.ForgetAllKnowledge();
        CloseFiles();
        FoundRows.Children.Clear();
        FoundCaption.Visibility = Visibility.Collapsed;
    }

    private void OnGoGeneral(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistant");

    // ── the files of a folder ────────────────────────────────────────────

    private void AskFiles(string folder)
    {
        _showing = folder;
        _filesAsked = DateTime.UtcNow;
        _app.RequestAssistantKnowledgeFiles(folder);
    }

    private void ShowFiles(KnowledgeFilesMessage files)
    {
        if (!string.Equals(files.Folder, _showing, StringComparison.OrdinalIgnoreCase)) return;
        var culture = Loc.Instance.Culture;
        FilesTitle.Text = Loc.T("knowledgeOptions.filesTitle", Path.GetFileName(files.Folder.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : files.Folder);
        FilesCaption.Text = files.Total > files.Files.Count
            ? Loc.T("knowledgeOptions.filesMore", files.Files.Count.ToString("N0", culture), files.Total.ToString("N0", culture))
            : Loc.T("knowledgeOptions.folder.files", files.Total.ToString("N0", culture));
        FileRows.Children.Clear();
        foreach (var file in files.Files) FileRows.Children.Add(FileRow(files.Folder, file));
        FilesCard.Visibility = Visibility.Visible;
        FilesCard.BringIntoView();
    }

    private Border FileRow(string folder, KnowledgeFileInfo file)
    {
        var culture = Loc.Instance.Culture;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var relative = Path.GetRelativePath(folder, file.Path);
        var name = new TextBlock { Text = relative, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Path, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        grid.Children.Add(name);
        var what = file.State switch
        {
            "ready" => Loc.T("knowledgeOptions.file.ready", file.Pieces.ToString("N0", culture))
                       + (file.Pages > 1 ? " · " + Loc.T("knowledgeOptions.file.pages", file.Pages.ToString("N0", culture)) : string.Empty)
                       + (file.Ocr ? " · OCR" : string.Empty),
            _ => Loc.T("knowledgeOptions.file." + file.State),
        };
        var state = new TextBlock { Style = (Style)FindResource("Caption"), Text = what, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (file.State == "failed") state.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);
        var row = new Border { Child = grid, Padding = new Thickness(0, 5, 0, 5) };
        AutomationProperties.SetName(row, relative + ": " + what);
        return row;
    }

    private void OnCloseFiles(object sender, RoutedEventArgs e) => CloseFiles();

    private void CloseFiles()
    {
        _showing = null;
        FilesCard.Visibility = Visibility.Collapsed;
        FileRows.Children.Clear();
    }

    // ── a search to try ──────────────────────────────────────────────────

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Search();
    }

    private void OnSearch(object sender, RoutedEventArgs e) => Search();

    private void Search()
    {
        var text = SearchBox.Text.Trim();
        if (text.Length < 2 || !_app.SearchAssistantKnowledge(text)) return;
        FoundCaption.Text = Loc.T("knowledgeOptions.searching");
        FoundCaption.Visibility = Visibility.Visible;
        FoundRows.Children.Clear();
    }

    private void ShowFound(KnowledgeFoundMessage found)
    {
        if (found.Text != SearchBox.Text.Trim()) return;
        var ms = found.Milliseconds.ToString("N0", Loc.Instance.Culture);
        FoundCaption.Text = found.Hits.Count == 0 ? Loc.T("knowledgeOptions.nothingFound")
            : Loc.T(found.How == "mixed" ? "knowledgeOptions.how.mixed" : "knowledgeOptions.how.words", ms);
        FoundCaption.Visibility = Visibility.Visible;
        FoundRows.Children.Clear();
        foreach (var hit in found.Hits) FoundRows.Children.Add(HitRow(hit));
    }

    private Border HitRow(KnowledgeHitInfo hit)
    {
        var panel = new StackPanel();
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = hit.Path };
        title.Text = Path.GetFileName(hit.Path) + (hit.Page > 1 || hit.Text.Length == 0 ? " · " + Loc.T("knowledgeOptions.page", hit.Page) : string.Empty);
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        panel.Children.Add(title);
        var text = hit.Text.ReplaceLineEndings(" ");
        panel.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), Text = text.Length > 320 ? text[..320] + "…" : text, Margin = new Thickness(0, 2, 0, 0) });
        var row = new Border { Child = panel, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(row, Path.GetFileName(hit.Path));
        return row;
    }
}
