using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace KDesktop.Core.Localization;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Runtime localisation. Languages are flat JSON files ("key": "text") named &lt;code&gt;.json, loaded from
/// the "Languages" folder next to the executable and from %APPDATA%\K-Desktop 5\Languages (user files win).
/// Adding a language = adding a file. Missing keys fall back to English, then to the key itself.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Fallback = "en";

    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changes.</summary>
    public event Action? Changed;

    public string Language { get; private set; } = Fallback;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(Fallback);

    /// <summary>Indexer used by XAML bindings: <c>{Binding [key], Source={x:Static Loc.Instance}}</c>.</summary>
    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string T(string key, params object?[] args)
    {
        var format = Instance.Get(key);
        try { return string.Format(Instance.Culture, format, args); }
        catch (FormatException) { return format; }
    }

    private static IEnumerable<string> Folders =>
    [
        Path.Combine(AppContext.BaseDirectory, "Languages"),
        Path.Combine(AppPaths.DataDir, "Languages"),
    ];

    public static IReadOnlyList<LanguageInfo> Available()
    {
        var result = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                var name = Read(file).TryGetValue("_language", out var n) ? n : code;
                result[code] = new LanguageInfo(code, name);
            }
        }
        return result.Values.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Language to use when the user has not chosen one: Spanish for Spanish Windows, English otherwise.</summary>
    public static string SystemDefault() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase) ? "es" : Fallback;

    public void Load(string? code)
    {
        code = string.IsNullOrWhiteSpace(code) ? SystemDefault() : code;
        _fallback = LoadMerged(Fallback);
        _strings = code.Equals(Fallback, StringComparison.OrdinalIgnoreCase) ? _fallback : LoadMerged(code);
        if (_strings.Count == 0) code = Fallback;
        Language = code;
        try { Culture = CultureInfo.GetCultureInfo(code); } catch { Culture = CultureInfo.InvariantCulture; }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        Changed?.Invoke();
    }

    private string Get(string key) =>
        _strings.TryGetValue(key, out var s) ? s : _fallback.TryGetValue(key, out var f) ? f : key;

    private static Dictionary<string, string> LoadMerged(string code)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in Folders)
        {
            var file = Path.Combine(folder, code + ".json");
            if (!File.Exists(file)) continue;
            foreach (var (k, v) in Read(file)) merged[k] = v;
        }
        return merged;
    }

    private static Dictionary<string, string> Read(string file)
    {
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(File.ReadAllText(file), options);
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) dict[p.Name] = p.Value.GetString()!;
            return dict;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer el idioma {file}: {ex.Message}");
            return [];
        }
    }
}
