using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Assistant;

/// <summary>
/// One MCP server, new or as it is (Options → Tools): a program (command, arguments one per line, folder) or an address
/// (and whether it only speaks the old SSE), its variables (a program) or headers (an address), each one plain or secret,
/// and how long a call may take and how long it stays running unused. A secret's value is never shown: left empty, the
/// one kept stays. What it does not know of the server's entry is kept as it was.
/// </summary>
public partial class McpServerWindow : FluentWindow
{
    private static readonly string[] Managed = ["command", "args", "cwd", "env", "url", "headers", "type", "timeout", "idleMinutes"];

    private readonly McpServerEntry? _server;
    private readonly IReadOnlyList<string> _others;
    private readonly ToolSecrets _secrets;
    private readonly List<ValueRow> _rows = [];

    /// <param name="others">The names of the other servers (a new name must not be one of them).</param>
    public McpServerWindow(McpServerEntry? server, IReadOnlyList<string> others, ToolSecrets secrets)
    {
        InitializeComponent();
        _server = server;
        _others = others;
        _secrets = secrets;
        Bar.Title = Loc.T(server is null ? "mcpServer.newTitle" : "mcpServer.editTitle");
        IdBox.Text = server?.Id ?? string.Empty;
        var isUrl = server?.Url is not null;
        (isUrl ? UrlRadio : ProgramRadio).IsChecked = true;
        CommandBox.Text = server?.Command ?? string.Empty;
        ArgsBox.Text = string.Join(Environment.NewLine, server?.Args ?? []);
        CwdBox.Text = server?.Cwd ?? string.Empty;
        UrlBox.Text = server?.Url ?? string.Empty;
        SseCheck.IsChecked = server?.Transport == "sse";
        if (server is not null)
        {
            TimeoutBox.Value = server.CallTimeout.TotalSeconds;
            IdleBox.Value = Math.Round(server.Idle.TotalMinutes);
            foreach (var (name, value) in isUrl ? server.Headers : server.Env) AddRow(name, value);
        }
        Kind();
    }

    /// <summary>The server as saved (null: cancelled).</summary>
    public McpServerEntry? Result { get; private set; }

    /// <summary>The secrets written in it (name → value), to keep encrypted.</summary>
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    private void OnKind(object sender, RoutedEventArgs e) => Kind();

    private void Kind()
    {
        if (ProgramPanel is null || UrlPanel is null) return;
        var url = UrlRadio.IsChecked == true;
        ProgramPanel.Visibility = url ? Visibility.Collapsed : Visibility.Visible;
        UrlPanel.Visibility = url ? Visibility.Visible : Visibility.Collapsed;
        ValuesTitle.Text = Loc.T(url ? "mcpServer.headers" : "mcpServer.env");
        ValuesHint.Text = Loc.T(url ? "mcpServer.headersHint" : "mcpServer.envHint");
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("mcpServer.programs") + "|*.exe;*.cmd;*.bat|*.*|*.*" };
        if (dialog.ShowDialog(this) == true) CommandBox.Text = dialog.FileName;
    }

    private void OnAddValue(object sender, RoutedEventArgs e) => AddRow(string.Empty, string.Empty);

    /// <summary>A variable or header: its name, its value (or a hidden one), whether it is a secret, and removing it.</summary>
    private void AddRow(string name, string value)
    {
        var kept = McpConfig.SecretsIn(value).FirstOrDefault() is { } secret && value.Trim() == "{{secret:" + secret + "}}" ? secret : null;
        var row = new ValueRow(kept);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Name = new TextBox { Text = name, PlaceholderText = Loc.T("mcpServer.valueName"), ClearButtonEnabled = false };
        AutomationProperties.SetName(row.Name, Loc.T("mcpServer.valueName"));
        row.Plain = new TextBox { Text = kept is null ? value : string.Empty, ClearButtonEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(row.Plain, Loc.T("mcpServer.value"));
        row.Hidden = new PasswordBox
        {
            Margin = new Thickness(8, 0, 0, 0),
            PlaceholderText = kept is not null && _secrets.Get(kept) is not null ? Loc.T("mcpServer.secretKept") : Loc.T("mcpServer.value"),
        };
        AutomationProperties.SetName(row.Hidden, Loc.T("mcpServer.value"));
        row.Secret = new CheckBox
        {
            Content = Loc.T("mcpServer.secret"),
            IsChecked = kept is not null,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        void Show()
        {
            var secret = row.Secret.IsChecked == true;
            row.Plain.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
            row.Hidden.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
        }
        row.Secret.Checked += (_, _) => Show();
        row.Secret.Unchecked += (_, _) => Show();
        // A name that says token, key, password…: ticked as secret while it is being written.
        row.Name.TextChanged += (_, _) =>
        {
            if (row.Secret.IsChecked != true && row.Plain.Text.Length == 0 && McpConfig.IsSecretName(row.Name.Text)) row.Secret.IsChecked = true;
        };
        var remove = new Button
        {
            Icon = ButtonIcons.For(SymbolRegular.Delete24),
            Appearance = ControlAppearance.Transparent,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = Loc.T("mcpServer.removeValue"),
        };
        AutomationProperties.SetName(remove, Loc.T("mcpServer.removeValue"));
        remove.Click += (_, _) =>
        {
            ValueRows.Children.Remove(grid);
            _rows.Remove(row);
        };
        Grid.SetColumn(row.Plain, 1);
        Grid.SetColumn(row.Hidden, 1);
        Grid.SetColumn(row.Secret, 2);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(row.Name);
        grid.Children.Add(row.Plain);
        grid.Children.Add(row.Hidden);
        grid.Children.Add(row.Secret);
        grid.Children.Add(remove);
        Show();
        ValueRows.Children.Add(grid);
        _rows.Add(row);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Secrets.Clear();
        var id = IdBox.Text.Trim();
        if (!McpConfig.IsId(id)) { Fail(Loc.T("mcpServer.badName")); return; }
        if (_others.Contains(id, StringComparer.OrdinalIgnoreCase)) { Fail(Loc.T("mcpServer.nameTaken", id)); return; }

        var raw = _server?.Raw.DeepClone().AsObject() ?? [];
        foreach (var field in Managed) raw.Remove(field);
        var url = UrlRadio.IsChecked == true;
        if (url)
        {
            if (UrlBox.Text.Trim().Length == 0) { Fail(Loc.T("mcpServer.noUrl")); return; }
            raw["url"] = UrlBox.Text.Trim();
            if (SseCheck.IsChecked == true) raw["type"] = "sse";
        }
        else
        {
            if (CommandBox.Text.Trim().Length == 0) { Fail(Loc.T("mcpServer.noCommand")); return; }
            raw["command"] = CommandBox.Text.Trim();
            var args = ArgsBox.Text.Split('\n').Select(a => a.Trim('\r', ' ')).Where(a => a.Length > 0).ToList();
            if (args.Count > 0) raw["args"] = new JsonArray([.. args.Select(a => (JsonNode)a)]);
            if (CwdBox.Text.Trim().Length > 0) raw["cwd"] = CwdBox.Text.Trim();
        }

        var values = new JsonObject();
        foreach (var row in _rows)
        {
            var name = row.Name.Text.Trim();
            if (name.Length == 0) continue;
            if (row.Secret.IsChecked == true)
            {
                var secret = row.Kept ?? $"{id}.{name}";
                if (!ToolSecrets.IsName(secret)) { Fail(Loc.T("mcpServer.badValueName", name)); return; }
                if (row.Hidden.Password.Length > 0) Secrets[secret] = row.Hidden.Password;
                else if (_secrets.Get(secret) is null) { Fail(Loc.T("mcpServer.noSecret", name)); return; }
                values[name] = "{{secret:" + secret + "}}";
            }
            else values[name] = row.Plain.Text;
        }
        if (values.Count > 0) raw[url ? "headers" : "env"] = values;
        if (TimeoutBox.Value is { } timeout && Math.Abs(timeout - 60) > 0.5) raw["timeout"] = Math.Round(timeout);
        if (IdleBox.Value is { } idle && Math.Abs(idle - 5) > 0.5) raw["idleMinutes"] = Math.Round(idle);

        var errors = PackageSchemas.Validate("mcp", new JsonObject { ["mcpServers"] = new JsonObject { [id] = raw.DeepClone() } }.ToJsonString());
        if (errors.Count > 0) { Fail(errors[0]); return; }
        Result = new McpServerEntry(id, raw);
        DialogResult = true;
    }

    private void Fail(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>One line of variables or headers.</summary>
    /// <param name="Kept">The secret it already had (its value is not shown), or null.</param>
    private sealed class ValueRow(string? kept)
    {
        public string? Kept { get; } = kept;

        public TextBox Name { get; set; } = null!;

        public TextBox Plain { get; set; } = null!;

        public PasswordBox Hidden { get; set; } = null!;

        public CheckBox Secret { get; set; } = null!;
    }
}
