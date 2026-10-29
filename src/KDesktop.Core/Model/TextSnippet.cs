using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    [ObservableProperty] private string? _notes;

    /// <summary>
    /// When not empty, it only goes off while one of these programs is in front (lowercase, without .exe); there it also
    /// works in the programs of «Desactivar en», and it wins over a text with the same abbreviation for everywhere.
    /// </summary>
    public ObservableCollection<string> Processes
    {
        get;
        set
        {
            if (field is not null) field.CollectionChanged -= OnProcessesChanged;
            field = value ?? [];
            field.CollectionChanged += OnProcessesChanged;
        }
    } = [];

    public TextSnippet() => Processes.CollectionChanged += OnProcessesChanged;

    /// <summary>Its first line with something, for the list.</summary>
    [JsonIgnore]
    public string Preview => Text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

    /// <summary>The programs it is limited to, one per line (empty: everywhere).</summary>
    [JsonIgnore]
    public string AppsList => string.Join(Environment.NewLine, Processes);

    [JsonIgnore]
    public bool HasApps => Processes.Count > 0;

    /// <summary>Raised whenever it changes (autosave).</summary>
    public event EventHandler? Modified;

    /// <summary>Whether it goes off with <paramref name="process"/> in front (its name in lowercase, without .exe).</summary>
    public bool WorksIn(string process) => Processes.Count == 0 || Processes.Contains(process, StringComparer.OrdinalIgnoreCase);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnProcessesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(AppsList)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasApps)));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An abbreviation as it can be typed: without spaces and without the prefix in front.</summary>
    public static string Clean(string abbreviation, string prefix)
    {
        var clean = new string(abbreviation.Where(c => !char.IsWhiteSpace(c)).ToArray());
        while (prefix.Length > 0 && clean.Length > prefix.Length && clean.StartsWith(prefix, StringComparison.Ordinal)) clean = clean[prefix.Length..];
        return clean;
    }

    /// <summary>Whether this and <paramref name="other"/> can go off in the same program (both everywhere, or a program in common).</summary>
    public bool SharesPrograms(TextSnippet other) =>
        Processes.Count == 0 && other.Processes.Count == 0 || Processes.Intersect(other.Processes, StringComparer.OrdinalIgnoreCase).Any();

    public TextSnippet Clone() => new() { Abbreviation = Abbreviation, Text = Text, Enabled = Enabled, Notes = Notes, Processes = [.. Processes] };
}
