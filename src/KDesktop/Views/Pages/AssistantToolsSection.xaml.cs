using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Views.Assistant;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Tools (stage 9.3): the MCP servers (state, test, stop, edit, switch off, remove, import, add),
/// every tool by group (built in, the user's JSON ones, each server's) with its switch, how it asks and a test with its
/// last result, the JSON tools' files with what is wrong with them, and the secrets (kept encrypted; their values never
/// shown). The rows are made in code (an ItemsControl hides its buttons from UI Automation).
/// </summary>
public partial class AssistantToolsSection : UserControl
{
    private const string GuideUrl = "https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/herramientas.md";

    private readonly App _app = App.Current;
    /// <summary>The tool groups shown open (the built-in ones start folded).</summary>
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closed = new(StringComparer.Ordinal) { "builtin" };
    /// <summary>The tool whose test box is open, and what was written in each.</summary>
    private string? _testing;
    private readonly Dictionary<string, string> _arguments = new(StringComparer.Ordinal);
    /// <summary>The secret being changed (its box shows).</summary>
    private string? _changingSecret;
    /// <summary>A box just opened: it takes the focus once.</summary>
    private bool _focusBox;

    public AssistantToolsSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantToolsChanged += Fill;
            _app.AssistantChanged += OnAssistantChanged;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantToolsChanged -= Fill;
            _app.AssistantChanged -= OnAssistantChanged;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _app.RequestAssistantTools();
            Fill();
        };
    }

    private void OnAssistantChanged()
    {
        if (!IsVisible) return;
        if (_app.AssistantTools is null) _app.RequestAssistantTools();
        Fill();
    }

    private bool Running => _app.Assistant is { IsRunning: true };

    /// <summary>Which page it is: <c>tools</c> (every tool), <c>tools/servers</c>, <c>tools/files</c> or <c>tools/secrets</c>.</summary>
    public string Part { get; set; } = "tools";

    private void Fill()
    {
        // Something being written keeps its focus when the rows are made again.
        var focused = Keyboard.FocusedElement is FrameworkElement { Tag: string tag } ? tag : null;
        OffBox.Visibility = Running ? Visibility.Collapsed : Visibility.Visible;
        OffText.Text = Loc.T(_app.Assistant is null ? "toolsOptions.moduleOff" : "toolsOptions.notRunning");
        GoGeneral.Visibility = _app.Assistant is null ? Visibility.Visible : Visibility.Collapsed;
        ServersCard.Visibility = Part == "tools/servers" ? Visibility.Visible : Visibility.Collapsed;
        ToolsCard.Visibility = Part == "tools" ? Visibility.Visible : Visibility.Collapsed;
        FilesCard.Visibility = Part == "tools/files" ? Visibility.Visible : Visibility.Collapsed;
        SecretsCard.Visibility = Part == "tools/secrets" ? Visibility.Visible : Visibility.Collapsed;
        switch (Part)
        {
            case "tools/servers": FillServers(); break;
            case "tools/files": FillFiles(); break;
            case "tools/secrets": FillSecrets(); break;
            default: FillTools(); break;
        }
        if (focused is not null && FindTagged(this, focused) is { } again)
            Dispatcher.BeginInvoke(() => again.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static FrameworkElement? FindTagged(DependencyObject parent, string tag)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Tag: string t } element && t == tag && element.Focusable) return element;
            if (FindTagged(child, tag) is { } found) return found;
        }
        return null;
    }

    // ── MCP servers ──────────────────────────────────────────────────────

    private void FillServers()
    {
        var servers = App.ReadMcpServers(out var errors);
        McpErrorBox.Visibility = errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        McpErrorText.Text = errors.Count > 0 ? Loc.T("toolsOptions.mcpBroken", errors[0]) : string.Empty;
        NoServers.Visibility = servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ServerRows.Children.Clear();
        var states = (_app.AssistantTools?.Servers ?? []).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var server in servers) ServerRows.Children.Add(ServerRow(server, states.GetValueOrDefault(server.Id)));
    }

    private Border ServerRow(McpServerEntry server, McpServerInfo? state)
    {
        var testing = _app.AssistantToolsTesting.Contains(server.Id);
        var running = state?.State == "running";
        var (status, color) = server.Disabled ? (Loc.T("toolsOptions.serverOff"), "TextFillColorSecondaryBrush")
            : testing || state?.State == "starting" ? (Loc.T("toolsOptions.serverStarting"), "Kd.AccentText")
            : state?.State == "failed" ? (Loc.T("toolsOptions.serverFailed", state.Error ?? string.Empty), "Kd.Danger")
            : running ? (Loc.T("toolsOptions.serverRunning"), "Kd.Success")
            : (Loc.T("toolsOptions.serverStopped"), "TextFillColorSecondaryBrush");
        var tools = state?.Tools ?? 0;

        var body = new StackPanel();
        body.Children.Add(Title(server.Id));
        body.Children.Add(Caption(server.Summary));
        var line = Caption(status + (state is not null && !server.Disabled ? " · " + Loc.T("toolsOptions.serverTools", tools) : string.Empty));
        line.SetResourceReference(TextBlock.ForegroundProperty, color);
        body.Children.Add(line);
        if (_app.AssistantToolTests.TryGetValue(server.Id, out var test) && !testing) body.Children.Add(Result(test));

        var buttons = Buttons();
        if (running) buttons.Children.Add(Small(SymbolRegular.Stop24, Loc.T("toolsOptions.stop"), () => _app.StopMcpServer(server.Id), Running, server.Id));
        else buttons.Children.Add(Small(SymbolRegular.Play24, Loc.T("toolsOptions.test"), () => _app.StartMcpServer(server.Id), Running && !server.Disabled && !testing, server.Id));
        buttons.Children.Add(Small(SymbolRegular.Edit24, Loc.T("toolsOptions.edit"), () => EditServer(server), true, server.Id));
        buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("toolsOptions.remove"), () => RemoveServer(server.Id), true, server.Id));
        var toggle = Toggle(!server.Disabled, Loc.T("toolsOptions.serverSwitch", server.Id), on => App.SetMcpServerDisabled(server.Id, !on));
        buttons.Children.Insert(0, toggle);
        return Row(SymbolRegular.PlugConnected24, body, buttons, server.Id);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var mcpo = @"E:\ProjectAI\Open WebUI\MCP";
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("toolsOptions.importTitle"),
            Filter = Loc.T("toolsOptions.importFilter") + "|*.json",
            InitialDirectory = Directory.Exists(mcpo) ? mcpo : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string json;
        try { json = File.ReadAllText(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = Say(Loc.T("toolsOptions.importTitle"), ex.Message); return; }
        var existing = App.ReadMcpServers(out _).Select(s => s.Id).ToList();
        var found = McpConfig.Import(json, existing);
        if (found.Count == 0)
        {
            _ = Say(Loc.T("toolsOptions.importTitle"), Loc.T("toolsOptions.importNone"));
            return;
        }
        var window = new McpImportWindow(found, Path.GetFileName(dialog.FileName)) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true && window.Chosen.Count > 0)
        {
            _app.ImportMcpServers(window.Chosen);
            Fill();
        }
    }

    private void OnAddServer(object sender, RoutedEventArgs e) => EditServer(null);

    private void EditServer(McpServerEntry? server)
    {
        var existing = App.ReadMcpServers(out _).Select(s => s.Id).Where(id => !string.Equals(id, server?.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var window = new McpServerWindow(server, existing, _app.ToolSecrets) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Result is not { } result) return;
        _app.SaveMcpServer(result, server?.Id, window.Secrets);
        Fill();
    }

    private async void RemoveServer(string id)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("toolsOptions.removeTitle"),
            Width = 480,
            MaxWidth = 480,
            Content = Loc.T("toolsOptions.removeQuestion", id),
            PrimaryButtonText = Loc.T("toolsOptions.remove"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        _app.RemoveMcpServer(id);
        Fill();
    }

    // ── tools ────────────────────────────────────────────────────────────

    private void OnFilter(object sender, TextChangedEventArgs e) => FillTools();

    private void FillTools()
    {
        ToolGroups.Children.Clear();
        var state = _app.AssistantTools;
        var filter = FilterBox.Text.Trim();
        var tools = (state?.Tools ?? []).Where(t => filter.Length == 0
            || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (t.Example?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        NoTools.Visibility = tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoTools.Text = Loc.T(state is null ? (Running ? "memoryOptions.loading" : "toolsOptions.noToolsOff") : "toolsOptions.noTools");
        // The user's first (their JSON ones, then each server's), the built-in ones last.
        var groups = tools.GroupBy(t => t.Group).OrderBy(g => g.Key == "json" ? 0 : g.Key == "builtin" ? 2 : 1).ToList();
        foreach (var group in groups) ToolGroups.Children.Add(Group(group.Key, [.. group], filter.Length > 0));
    }

    private UIElement Group(string group, IReadOnlyList<ToolInfo> tools, bool filtering)
    {
        var title = group switch
        {
            "builtin" => Loc.T("toolsOptions.groupBuiltin"),
            "json" => Loc.T("toolsOptions.groupJson"),
            _ => Loc.T("toolsOptions.groupMcp", group["mcp:".Length..]),
        };
        var off = tools.Count(t => !t.Enabled);
        var expander = new Expander
        {
            Header = $"{title} · {tools.Count}" + (off > 0 ? " · " + Loc.T("toolsOptions.groupOff", off) : string.Empty),
            IsExpanded = filtering || (!_closed.Contains(group) || _open.Contains(group)),
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetName(expander, title);
        expander.Expanded += (_, _) => { if (!filtering) { _open.Add(group); _closed.Remove(group); } };
        expander.Collapsed += (_, _) => { if (!filtering) { _closed.Add(group); _open.Remove(group); } };
        var rows = new StackPanel { Margin = new Thickness(4, 4, 0, 0) };
        foreach (var tool in tools) rows.Children.Add(ToolRow(tool));
        expander.Content = rows;
        return expander;
    }

    private Border ToolRow(ToolInfo tool)
    {
        var body = new StackPanel();
        var title = Title(tool.Name);
        if (!tool.Enabled) title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        body.Children.Add(title);
        body.Children.Add(Caption(tool.Example is { Length: > 0 } example ? "«" + example + "»" : Shorten(OneLine(tool.Description), 160)));
        var testing = _app.AssistantToolsTesting.Contains(tool.Name);
        if (_testing == tool.Name) body.Children.Add(TestBox(tool));
        if (testing) body.Children.Add(Caption(Loc.T("toolsOptions.testing")));
        else if (_app.AssistantToolTests.TryGetValue(tool.Name, out var test)) body.Children.Add(Result(test));

        var buttons = Buttons();
        buttons.Children.Add(Toggle(tool.Enabled, Loc.T("toolsOptions.toolSwitch", tool.Name), on => _app.SetToolEnabled(tool.Name, on)));
        buttons.Children.Add(RiskBox(tool));
        buttons.Children.Add(Small(SymbolRegular.Play24, Loc.T("toolsOptions.test"), () =>
        {
            _testing = _testing == tool.Name ? null : tool.Name;
            _focusBox = _testing is not null;
            Fill();
        }, Running && tool.Enabled, tool.Name));
        var icon = tool.Group == "builtin" ? SymbolRegular.Wrench24 : tool.Group == "json" ? SymbolRegular.DocumentData24 : SymbolRegular.PlugConnected24;
        return Row(icon, body, buttons, tool.Name);
    }

    private ComboBox RiskBox(ToolInfo tool)
    {
        var box = new ComboBox { Width = 230, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, Loc.T("toolsOptions.risk", tool.Name));
        foreach (var risk in new[] { "safe", "askOnce", "askAlways" })
        {
            var text = Loc.T("toolsOptions.risk." + risk) + (risk == tool.DefaultRisk ? " " + Loc.T("toolsOptions.riskDefault") : string.Empty);
            var item = new ComboBoxItem { Content = text, Tag = risk };
            box.Items.Add(item);
            if (risk == tool.Risk) box.SelectedItem = item;
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not ComboBoxItem { Tag: string risk } || risk == tool.Risk) return;
            _app.SetToolRisk(tool.Name, risk == tool.DefaultRisk ? null : risk);
        };
        return box;
    }

    private StackPanel TestBox(ToolInfo tool)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            Text = _arguments.GetValueOrDefault(tool.Name, "{}"),
            PlaceholderText = "{ \"…\": \"…\" }",
            ClearButtonEnabled = false,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Tag = "test:" + tool.Name,
        };
        AutomationProperties.SetName(box, Loc.T("toolsOptions.arguments"));
        box.TextChanged += (_, _) => _arguments[tool.Name] = box.Text;
        void Run() => _app.TestAssistantTool(tool.Name, box.Text);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Run();
        };
        grid.Children.Add(box);
        var run = new Button { Content = Loc.T("toolsOptions.run"), Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 }, Margin = new Thickness(8, 0, 0, 0), Appearance = ControlAppearance.Primary };
        run.Click += (_, _) => Run();
        AutomationProperties.SetName(run, Loc.T("toolsOptions.run") + " " + tool.Name);
        Grid.SetColumn(run, 1);
        grid.Children.Add(run);
        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(Caption(Loc.T("toolsOptions.argumentsHint")));
        if (_focusBox)
        {
            _focusBox = false;
            Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
        return stack;
    }

    private TextBlock Result(ToolTestMessage test)
    {
        var text = Caption((test.Ok ? "✓ " : "✗ ") + Shorten(test.Text.ReplaceLineEndings(" · "), 400) + $"  ({test.Ms} ms)");
        text.SetResourceReference(TextBlock.ForegroundProperty, test.Ok ? "Kd.Success" : "Kd.Danger");
        text.TextWrapping = TextWrapping.Wrap;
        return text;
    }

    // ── JSON tool files ──────────────────────────────────────────────────

    private void FillFiles()
    {
        var files = _app.AssistantTools?.Files ?? [];
        NoFiles.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileRows.Children.Clear();
        foreach (var file in files)
        {
            var body = new StackPanel();
            body.Children.Add(Title(file.Name ?? Path.GetFileName(file.File)));
            body.Children.Add(Caption(Path.GetFileName(file.File) + (file.Type is { } type ? " · " + Loc.T("toolsOptions.type." + type) : string.Empty)
                + (file.Enabled || file.Errors.Count > 0 ? string.Empty : " · " + Loc.T("toolsOptions.fileOff"))));
            foreach (var error in file.Errors.Take(3))
            {
                var line = Caption(error);
                line.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
                body.Children.Add(line);
            }
            var buttons = Buttons();
            buttons.Children.Add(Small(SymbolRegular.Edit24, Loc.T("toolsOptions.editFile"), () => App.OpenToolFile(file.File), true, Path.GetFileName(file.File)));
            FileRows.Children.Add(Row(file.Errors.Count > 0 ? SymbolRegular.ErrorCircle24 : SymbolRegular.DocumentData24, body, buttons, file.File));
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => App.OpenToolsFolder();

    private void OnNewTool(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = NewToolButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var type in new[] { "http", "kdesktop", "routine", "script" })
        {
            var item = new MenuItem { Header = Loc.T("toolsOptions.type." + type) };
            item.Click += (_, _) => App.NewToolFromTemplate(type);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnGuide(object sender, RoutedEventArgs e) => ProcessLauncher.Launch(new LaunchSpec(GuideUrl), asAdmin: false);

    // ── secrets ──────────────────────────────────────────────────────────

    private void FillSecrets()
    {
        var secrets = _app.ToolSecretList();
        NoSecrets.Visibility = secrets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SecretRows.Children.Clear();
        foreach (var (name, kept, usedBy) in secrets)
        {
            var body = new StackPanel();
            body.Children.Add(Title(name));
            var line = Caption((kept ? Loc.T("toolsOptions.secretKept") : Loc.T("toolsOptions.secretMissing"))
                + (usedBy.Count > 0 ? " · " + Loc.T("toolsOptions.secretUsedBy", string.Join(", ", usedBy)) : " · " + Loc.T("toolsOptions.secretUnused")));
            if (!kept) line.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
            body.Children.Add(line);
            var buttons = Buttons();
            if (_changingSecret == name)
            {
                var box = new PasswordBox { Width = 240, PlaceholderText = Loc.T("toolsOptions.secretValue"), Tag = "secret:" + name };
                AutomationProperties.SetName(box, Loc.T("toolsOptions.secretValue"));
                void Save()
                {
                    if (box.Password.Length > 0) _app.SetToolSecret(name, box.Password);
                    _changingSecret = null;
                    Fill();
                }
                box.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) Save();
                    else if (e.Key == Key.Escape) { _changingSecret = null; Fill(); }
                    else return;
                    e.Handled = true;
                };
                buttons.Children.Add(box);
                buttons.Children.Add(Small(SymbolRegular.Checkmark24, Loc.T("memoryOptions.save"), Save, true));
                buttons.Children.Add(Small(SymbolRegular.Dismiss24, Loc.T("common.cancel"), () => { _changingSecret = null; Fill(); }, true));
                if (_focusBox)
                {
                    _focusBox = false;
                    Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
            else
            {
                var change = new Button { Content = Loc.T(kept ? "toolsOptions.secretChange" : "toolsOptions.secretSet"), Margin = new Thickness(4, 0, 0, 0) };
                AutomationProperties.SetName(change, Loc.T(kept ? "toolsOptions.secretChange" : "toolsOptions.secretSet") + " " + name);
                change.Click += (_, _) => { _changingSecret = name; _focusBox = true; Fill(); };
                buttons.Children.Add(change);
                if (kept) buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("toolsOptions.secretDelete"), () => { _app.SetToolSecret(name, null); Fill(); }, true, name));
            }
            SecretRows.Children.Add(Row(SymbolRegular.Key24, body, buttons, name));
        }
    }

    private void OnAddSecret(object sender, RoutedEventArgs e)
    {
        var name = NewSecretName.Text.Trim();
        if (!ToolSecrets.IsName(name) || NewSecretValue.Password.Length == 0)
        {
            _ = Say(Loc.T("toolsOptions.secrets"), Loc.T("toolsOptions.secretBadName"));
            return;
        }
        _app.SetToolSecret(name, NewSecretValue.Password);
        NewSecretName.Text = string.Empty;
        NewSecretValue.Password = string.Empty;
        Fill();
    }

    // ── pieces ───────────────────────────────────────────────────────────

    private static Border Row(SymbolRegular symbol, UIElement body, UIElement buttons, string name)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 18, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        grid.Children.Add(icon);
        Grid.SetColumn((FrameworkElement)body, 1);
        grid.Children.Add(body);
        Grid.SetColumn((FrameworkElement)buttons, 2);
        grid.Children.Add(buttons);
        var row = new Border { Child = grid, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "Kd.Subtle");
        AutomationProperties.SetName(row, name);
        return row;
    }

    private static StackPanel Buttons() =>
        new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };

    private static TextBlock Title(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    private TextBlock Caption(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };

    private static ToggleSwitch Toggle(bool on, string name, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        AutomationProperties.SetName(toggle, name);
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        return toggle;
    }

    /// <param name="target">What it acts on, for its accessible name («Probar jokes»).</param>
    private static Button Small(SymbolRegular symbol, string name, Action click, bool enabled, string? target = null)
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
        AutomationProperties.SetName(button, target is null ? name : name + " " + target);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>On one line, without runs of spaces (descriptions of servers come with their line breaks).</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Shorten(string text, int most) => text.Length <= most ? text : text[..most].TrimEnd() + "…";

    private static Task Say(string title, string text) => new Wpf.Ui.Controls.MessageBox
    {
        Title = title,
        Width = 480,
        MaxWidth = 480,
        Content = text,
        CloseButtonText = Loc.T("common.close"),
    }.ShowDialogAsync();

    private void OnGoGeneral(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistant");
}
