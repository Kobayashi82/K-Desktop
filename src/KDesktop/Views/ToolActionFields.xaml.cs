using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Views.Assistant;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views;

/// <summary>A tool of the editor's list: its name for people, where it comes from and its icon, as Options → Tools shows them.</summary>
public sealed record ToolChoice(string Name, string Title, string Origin, SymbolRegular Symbol, ToolInfo Info)
{
    public override string ToString() => Title;
}

/// <summary>
/// The editor of «Asistente: usar una herramienta» (G1, its DataContext): the tools the assistants' process has, but those
/// K-Desktop already has as an action (<see cref="AssistantToolAction.Covered"/>) or that are off; under it its
/// description, a warning when it is gone or asks, and its fields from its schema (<see cref="ToolTestForm"/> with
/// variables), or its JSON. What is written goes into <see cref="AssistantToolAction.Arguments"/> as it is written.
/// </summary>
public partial class ToolActionFields : UserControl
{
    private ToolTestForm? _form;
    private bool _asJson;
    private bool _filling;
    private string _shown = string.Empty;

    public ToolActionFields()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _asJson = false;
            Fill();
        };
        Loaded += (_, _) => App.Current.AssistantToolsChanged += OnToolsChanged;
        Unloaded += (_, _) => App.Current.AssistantToolsChanged -= OnToolsChanged;
    }

    private AssistantToolAction? Model => DataContext as AssistantToolAction;

    /// <summary>The tools to choose from, in alphabetical order (the user's language).</summary>
    public static IReadOnlyList<ToolChoice> Choices() =>
        [.. (App.Current.AssistantTools?.Tools ?? [])
            .Where(t => t.Enabled && !(t.Group == "builtin" && AssistantToolAction.Covered.Contains(t.Name)))
            .Select(t => new ToolChoice(t.Name, AssistantToolsSection.TitleOf(t), OriginOf(t), IconOf(t), t))
            .OrderBy(c => c.Title, AssistantToolsSection.Alphabetical)];

    private static string OriginOf(ToolInfo tool) => tool.Group switch
    {
        "builtin" => Loc.T("toolsOptions.subject." + AssistantToolsSection.SubjectOfName(tool.Name)),
        "json" => Loc.T("settings.nav.toolFiles"),
        _ => tool.Group.StartsWith("mcp:", StringComparison.Ordinal) ? tool.Group[4..] : tool.Group,
    };

    private static SymbolRegular IconOf(ToolInfo tool) => tool.Group switch
    {
        "builtin" => AssistantToolsSection.SubjectIcon(AssistantToolsSection.SubjectOfName(tool.Name)),
        "json" => AssistantToolsSection.TypeIcon(App.Current.AssistantTools?.Files.FirstOrDefault(f => f.Name == tool.Name)?.Type),
        _ => SymbolRegular.PlugConnected24,
    };

    /// <summary>The process said something new: the list again, but only if the tools changed (a server starting is not that).</summary>
    private void OnToolsChanged()
    {
        if (!IsLoaded || Model is null) return;
        if (Signature() != _shown) Fill();
    }

    private static string Signature() =>
        App.Current.AssistantTools is { } state ? string.Join('|', Choices().Select(c => c.Name + ":" + c.Info.Risk + ":" + c.Info.Schema?.Length)) : "-";

    private void Fill()
    {
        if (Model is not { } action) return;
        _filling = true;
        try
        {
            _shown = Signature();
            var choices = Choices();
            ToolBox.ItemsSource = choices;
            ToolBox.SelectedItem = choices.FirstOrDefault(c => c.Name == action.Tool);
            if (ToolBox.SelectedItem is ToolChoice chosen) action.ToolName = chosen.Title;
        }
        finally { _filling = false; }
        ShowTool();
    }

    private void OnToolPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Model is not { } action || ToolBox.SelectedItem is not ToolChoice chosen || chosen.Name == action.Tool) return;
        action.ToolName = chosen.Title;
        action.Arguments = "{}";
        action.Tool = chosen.Name;
        _asJson = false;
        ShowTool();
    }

    private void ShowTool()
    {
        if (Model is not { } action) return;
        var chosen = ToolBox.SelectedItem as ToolChoice;
        var state = App.Current.AssistantTools;

        // Its description: a phrase that asks for it says it best, in the user's language; the model's is in English.
        var about = chosen is null ? string.Empty
            : chosen.Info.Example is { Length: > 0 } example ? Loc.T("toolsOptions.example", example) : OneLine(chosen.Info.Description);
        Description.Text = about.Length > 300 ? about[..300].TrimEnd() + "…" : about;
        Description.ToolTip = about.Length > 300 ? new System.Windows.Controls.ToolTip { Content = new TextBlock { Text = about, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 } } : null;
        Description.Visibility = about.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Not there: the process is not up yet, or the tool went (its server removed, switched off).
        Missing.Text = state is null ? Loc.T(App.Current.Assistant is { IsRunning: true } ? "memoryOptions.loading" : "toolsOptions.noToolsOff")
            : chosen is null && action.Tool.Length > 0 ? Loc.T("editor.toolMissing", action.Tool) : string.Empty;
        Missing.Visibility = Missing.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // What still asks from a command: what the user set to always ask, and a script not approved yet.
        var script = chosen is { Info.Group: "json" } && state?.Files.FirstOrDefault(f => f.Name == chosen.Name)?.Type == "script";
        Asks.Text = chosen is { Info: { Risk: "askAlways", DefaultRisk: not "askAlways" } } ? Loc.T("editor.toolAsks")
            : script ? Loc.T("editor.toolScriptAsks") : string.Empty;
        Asks.Visibility = Asks.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Its fields, or its JSON (asked for, or a tool that is not there now: what it has is not lost).
        _form = new ToolTestForm(chosen?.Info.Schema, ToolTestForm.ValuesOf(action.Arguments), variables: true);
        _form.Changed += () =>
        {
            action.Arguments = _form.ToArguments();
            ShowProblem(_form.Problem());
        };
        var json = _asJson || (chosen is null && action.Tool.Length > 0) || (chosen is { Info.Schema: null } && !_form.HasFields);
        FieldsHost.Content = !json && _form.HasFields ? _form.View : null;
        JsonBox.Visibility = json ? Visibility.Visible : Visibility.Collapsed;
        if (json)
        {
            _filling = true;
            JsonBox.Text = Pretty(action.Arguments);
            _filling = false;
        }
        NoFields.Visibility = chosen is not null && !json && !_form.HasFields ? Visibility.Visible : Visibility.Collapsed;
        SwapButton.Visibility = chosen is not null && _form.HasFields ? Visibility.Visible : Visibility.Collapsed;
        SwapButton.Content = Loc.T(json ? "toolsOptions.form.asForm" : "toolsOptions.form.asJson");
        SwapButton.Icon = new SymbolIcon { Symbol = json ? SymbolRegular.Form24 : SymbolRegular.Code24 };
        ShowProblem(json ? JsonProblem(JsonBox.Text) : _form.Problem());
    }

    private void OnJsonChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || Model is not { } action || JsonBox.Visibility != Visibility.Visible) return;
        action.Arguments = JsonBox.Text.Trim().Length == 0 ? "{}" : JsonBox.Text;
        ShowProblem(JsonProblem(JsonBox.Text));
    }

    private void OnSwap(object sender, RoutedEventArgs e)
    {
        if (Model is not { } action) return;
        // Back to the form only with good JSON (it would be lost otherwise).
        if (_asJson && JsonProblem(JsonBox.Text) is { } problem)
        {
            ShowProblem(problem);
            return;
        }
        if (_asJson) action.Arguments = Compact(JsonBox.Text);
        _asJson = !_asJson;
        ShowTool();
    }

    private void ShowProblem(string? problem)
    {
        Problem.Text = problem ?? string.Empty;
        Problem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? JsonProblem(string text)
    {
        if (text.Trim().Length == 0) return null;
        try { return JsonNode.Parse(text) is JsonObject ? null : Loc.T("toolsOptions.form.badJson"); }
        catch (JsonException) { return Loc.T("toolsOptions.form.badJson"); }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Pretty(string arguments)
    {
        try { return JsonNode.Parse(arguments) is JsonObject { Count: > 0 } o ? o.ToJsonString(Indented) : "{}"; }
        catch (JsonException) { return arguments; }
    }

    private static string Compact(string text)
    {
        try { return JsonNode.Parse(text)?.ToJsonString() ?? "{}"; }
        catch (JsonException) { return text; }
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
