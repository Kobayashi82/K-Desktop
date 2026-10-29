using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Core.Model;

/// <summary>
/// A text of the expander («Textos»): typing the prefix and <see cref="Abbreviation"/> anywhere writes <see cref="Text"/>
/// instead, with the variables of the commands ({fecha}, {portapapeles}…) and {cursor} where the cursor stays.
/// </summary>
public partial class TextSnippet : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();

    /// <summary>What is typed after the prefix («firma» for «;firma»); no spaces.</summary>
    [ObservableProperty] private string _abbreviation = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Preview))] private string _text = string.Empty;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private DateTime _created = DateTime.Now;

    /// <summary>Its first line with something, for the list.</summary>
    [JsonIgnore]
    public string Preview => Text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

    /// <summary>Raised whenever it changes (autosave).</summary>
    public event EventHandler? Modified;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An abbreviation as it can be typed: without spaces and without the prefix in front.</summary>
    public static string Clean(string abbreviation, string prefix)
    {
        var clean = new string(abbreviation.Where(c => !char.IsWhiteSpace(c)).ToArray());
        while (prefix.Length > 0 && clean.Length > prefix.Length && clean.StartsWith(prefix, StringComparison.Ordinal)) clean = clean[prefix.Length..];
        return clean;
    }

    public TextSnippet Clone() => new() { Abbreviation = Abbreviation, Text = Text, Enabled = Enabled };
}
