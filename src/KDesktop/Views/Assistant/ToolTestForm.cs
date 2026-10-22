using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Localization;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The form of «Probar» in Options → Tools (5.0.133): one field per parameter of the tool's JSON schema —a list for an
/// enum, yes/no for a boolean, a box for the rest—, with its description, its default and a mark on the required ones;
/// it gives back the arguments as JSON, typed, with only what was filled in. What was written is kept by the caller
/// (<see cref="Values"/>) so the rows can be made again without losing it.
/// </summary>
public sealed class ToolTestForm
{
    private sealed record Field(string Name, string Type, bool Required, IReadOnlyList<JsonNode?>? Choices, Func<string> Read);

    private readonly List<Field> _fields = [];

    /// <param name="schema">The tool's parameters (JSON schema of an object); null or broken: no fields.</param>
    /// <param name="values">What was written in each field (kept between rebuilds; changed as the user writes).</param>
    public ToolTestForm(string? schema, Dictionary<string, string> values)
    {
        Values = values;
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        View = panel;
        if (Parse(schema) is not { } root || root["properties"] is not JsonObject properties) return;
        var required = root["required"] is JsonArray list ? list.Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal) : [];
        foreach (var (name, node) in properties)
        {
            if (node is not JsonObject property) continue;
            var field = Make(name, property, required.Contains(name), out var control, out var hint);
            _fields.Add(field);
            var row = panel.RowDefinitions.Count;
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new StackPanel { Margin = new Thickness(0, 7, 12, 0) };
            var title = new TextBlock { Text = Humanize(name) + (field.Required ? " *" : string.Empty), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            label.Children.Add(title);
            var technical = new TextBlock { Text = name, FontSize = 11, FontFamily = new FontFamily("Consolas") };
            technical.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            label.Children.Add(technical);
            Grid.SetRow(label, row);
            panel.Children.Add(label);

            var right = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            AutomationProperties.SetName(control, Humanize(name));
            right.Children.Add(control);
            if (hint.Length > 0)
            {
                var caption = new TextBlock { Text = hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) };
                caption.SetResourceReference(TextBlock.ForegroundProperty, "Kd.TextDim");
                right.Children.Add(caption);
            }
            Grid.SetRow(right, row);
            Grid.SetColumn(right, 1);
            panel.Children.Add(right);
        }
    }

    /// <summary>The fields, ready to be shown (empty when the tool takes nothing).</summary>
    public FrameworkElement View { get; }

    public bool HasFields => _fields.Count > 0;

    public Dictionary<string, string> Values { get; }

    /// <summary>The first field that takes the focus.</summary>
    public FrameworkElement? First { get; private set; }

    /// <summary>The arguments as JSON, or null with <paramref name="error"/> saying what is missing or wrong.</summary>
    public string? ToJson(out string? error)
    {
        var arguments = new JsonObject();
        foreach (var field in _fields)
        {
            var text = field.Read().Trim();
            if (text.Length == 0)
            {
                if (field.Required)
                {
                    error = Loc.T("toolsOptions.form.missing", Humanize(field.Name));
                    return null;
                }
                continue;
            }
            if (Value(field, text) is not { } value)
            {
                error = Loc.T("toolsOptions.form.wrong", Humanize(field.Name));
                return null;
            }
            arguments[field.Name] = value;
        }
        error = null;
        return arguments.ToJsonString();
    }

    private static JsonNode? Value(Field field, string text)
    {
        if (field.Choices is { } choices && choices.FirstOrDefault(c => Show(c) == text) is { } chosen) return chosen?.DeepClone();
        switch (field.Type)
        {
            case "boolean":
                return text == "true" ? JsonValue.Create(true) : text == "false" ? JsonValue.Create(false) : null;
            case "integer":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) ? JsonValue.Create(whole) : null;
            case "number":
                return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : null;
            case "array" or "object":
                try { return JsonNode.Parse(text); }
                catch (JsonException) { return null; }
            default:
                return JsonValue.Create(text);
        }
    }

    private Field Make(string name, JsonObject property, bool required, out FrameworkElement control, out string hint)
    {
        var type = TypeOf(property);
        var description = property["description"]?.GetValue<string>() is { } d ? OneLine(d) : string.Empty;
        var fallback = property["default"] is { } def ? Show(def) : null;
        var example = property["examples"] is JsonArray { Count: > 0 } examples ? Show(examples[0]) : null;
        hint = description;
        if (fallback is { Length: > 0 }) hint = (hint.Length > 0 ? hint + " · " : string.Empty) + Loc.T("toolsOptions.form.default", fallback);
        var saved = Values.TryGetValue(name, out var was) ? was : null;

        // A list: its values (and «nothing» when it may be left out), or yes / no.
        var choices = property["enum"] is JsonArray values ? values.ToList()
            : type == "boolean" ? [JsonValue.Create(true), JsonValue.Create(false)]
            : null;
        if (choices is not null)
        {
            var box = new ComboBox { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
            if (!required) box.Items.Add(new ComboBoxItem { Content = Loc.T("toolsOptions.form.none"), Tag = string.Empty });
            foreach (var choice in choices)
            {
                var shown = Show(choice);
                var text = type == "boolean" ? Loc.T(shown == "true" ? "toolsOptions.form.yes" : "toolsOptions.form.no") : shown;
                box.Items.Add(new ComboBoxItem { Content = text, Tag = shown });
            }
            var start = saved ?? fallback ?? string.Empty;
            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == start) ?? box.Items[0];
            box.SelectionChanged += (_, _) => Values[name] = Read();
            Values[name] = Read();
            control = box;
            First ??= box;
            return new Field(name, type, required, choices, Read);
            string Read() => box.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : string.Empty;
        }

        var multiline = type == "string" && (Wordy(name) || description.Length > 0 && Wordy(description.Split(' ')[0]));
        var input = new TextBox
        {
            Text = saved ?? string.Empty,
            PlaceholderText = fallback ?? example ?? type switch
            {
                "integer" or "number" => "0",
                "array" => "[ … ]",
                "object" => "{ … }",
                _ => string.Empty,
            },
            ClearButtonEnabled = false,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinLines = multiline ? 2 : 1,
            MaxLines = multiline ? 6 : 1,
            Tag = "field:" + name,
        };
        if (type is "array" or "object" or "integer" or "number") input.FontFamily = new FontFamily("Consolas");
        if (type is "integer" or "number") { input.MaxWidth = 200; input.HorizontalAlignment = HorizontalAlignment.Left; input.MinWidth = 200; }
        input.TextChanged += (_, _) => Values[name] = input.Text;
        control = input;
        First ??= input;
        return new Field(name, type, required, null, () => input.Text);
    }

    /// <summary>Its type, the first one that is not null («["string","null"]»).</summary>
    private static string TypeOf(JsonObject property) => property["type"] switch
    {
        JsonValue value when value.TryGetValue<string>(out var one) => one,
        JsonArray many => many.Select(n => n?.GetValue<string>()).FirstOrDefault(t => t is not null and not "null") ?? "string",
        _ => "string",
    };

    /// <summary>A long text is expected: a message, a question, a note…</summary>
    private static bool Wordy(string name) => WordyNames.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] WordyNames = ["text", "message", "body", "content", "prompt", "question", "note", "fact", "caption", "texto", "mensaje"];

    private static string Show(JsonNode? node) => node switch
    {
        null => "null",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        _ => node.ToJsonString(),
    };

    /// <summary>«start_date» → «Start date», «filePath» → «File path».</summary>
    public static string Humanize(string name)
    {
        var spaced = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '_' or '-' or '.') { spaced.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1])) spaced.Append(' ');
            spaced.Append(spaced.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return spaced.ToString().Trim();
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static JsonObject? Parse(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema)) return null;
        try { return JsonNode.Parse(schema) as JsonObject; }
        catch (JsonException) { return null; }
    }
}
