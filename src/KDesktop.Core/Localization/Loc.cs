using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Localization;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Runtime localisation. Languages are flat JSON files ("key": "text") named &lt;code&gt;.json: Spanish and English are
/// built into the assembly, and %APPDATA%\K-Desktop 5\Languages adds languages or overrides texts key by key (user files win).
/// Adding a language = adding a file. Missing keys fall back to English, then to the key itself.
/// </summary>
public sealed partial class Loc : INotifyPropertyChanged
{
    public const string Fallback = "en";

    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changes.</summary>
    public event Action? Changed;

    private string _language = Fallback;
    private CultureInfo _culture = CultureInfo.GetCultureInfo(Fallback);

    /// <summary>The language of what runs now: the one of <see cref="Use"/> around it, or the loaded one.</summary>
    public string Language => _scope.Value?.Language ?? _language;

    public CultureInfo Culture => _scope.Value?.Culture ?? _culture;

    /// <summary>Another language for one flow of work (an assistant that speaks in its own, 5.0.105).</summary>
    private sealed record Scoped(string Language, CultureInfo Culture, Dictionary<string, string> Strings);

    private readonly AsyncLocal<Scoped?> _scope = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Scoped> _scopes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What runs inside (and everything it starts, awaits included) uses <paramref name="code"/> instead of the loaded
    /// language, until the result is disposed; null, empty or the loaded one: nothing changes. The rest of the process
    /// keeps its language (the assistant process: one assistant answers in English while another does in Spanish).
    /// </summary>
    public LanguageScope Use(string? code)
    {
        var before = _scope.Value;
        if (string.IsNullOrWhiteSpace(code) || code.Equals(Language, StringComparison.OrdinalIgnoreCase)) return new LanguageScope(this, before, false);
        _scope.Value = code.Equals(_language, StringComparison.OrdinalIgnoreCase) ? null : _scopes.GetOrAdd(code, c =>
        {
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(c); } catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
            return new Scoped(c, culture, c.Equals(Fallback, StringComparison.OrdinalIgnoreCase) ? LoadMerged(Fallback) : LoadMerged(c));
        });
        return new LanguageScope(this, before, true);
    }

    /// <summary>Back to the language there was before <see cref="Use"/>.</summary>
    public readonly struct LanguageScope : IDisposable
    {
        private readonly Loc? _owner;
        private readonly object? _before;
        private readonly bool _changed;

        internal LanguageScope(Loc owner, object? before, bool changed)
        {
            _owner = owner;
            _before = before;
            _changed = changed;
        }

        public void Dispose()
        {
            if (_changed && _owner is not null) _owner._scope.Value = (Scoped?)_before;
        }
    }

    /// <summary>Indexer used by XAML bindings: <c>{Binding [key], Source={x:Static Loc.Instance}}</c>.</summary>
    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string T(string key, params object?[] args)
    {
        var format = Instance.Get(key);
        try { return string.Format(Instance.Culture, format, args); }
        catch (FormatException) { return format; }
    }

    private const string EmbeddedFolder = "Languages";

    public static IReadOnlyList<LanguageInfo> Available()
    {
        var result = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string code, Dictionary<string, string> strings)
        {
            // A user file that only overrides a few texts keeps the built-in language name.
            var name = strings.TryGetValue("_language", out var n) ? n : result.TryGetValue(code, out var known) ? known.Name : code;
            result[code] = new LanguageInfo(code, name);
        }

        foreach (var file in EmbeddedAssets.List(EmbeddedFolder))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            Add(code, ReadEmbedded(code));
        }
        foreach (var file in UserFiles())
            Add(Path.GetFileNameWithoutExtension(file), ReadFile(file));
        return result.Values.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string[] UserFiles()
    {
        try { return Directory.Exists(AppPaths.LanguagesDir) ? Directory.GetFiles(AppPaths.LanguagesDir, "*.json") : []; }
        catch { return []; }
    }

    /// <summary>
    /// The texts of the language in use as the starting point of a translation: laid out like the built-in English file
    /// (its order and blank lines), English where the language has no text, and "_language" set to <paramref name="code"/>
    /// so the new language does not show up as a second one of the same name.
    /// </summary>
    public static string Template(string code)
    {
        var json = EmbeddedAssets.ReadText(EmbeddedFolder, Fallback + ".json") ?? "{}";
        var current = Instance._strings;
        var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return TemplateLine().Replace(json, m => m.Groups[2].Value switch
        {
            "_language" => $"{m.Groups[1].Value}{JsonSerializer.Serialize(code, options)}{m.Groups[3].Value}",
            var key when current.TryGetValue(key, out var text) => $"{m.Groups[1].Value}{JsonSerializer.Serialize(text, options)}{m.Groups[3].Value}",
            _ => m.Value,
        });
    }

    /// <summary>One <c>"key": "text",</c> line of a language file (groups: up to the text, the key, the end of the line).</summary>
    [GeneratedRegex("""^(\s*"([^"\\]+)"\s*:\s*)"(?:[^"\\]|\\.)*"(\s*,?\s*)$""", RegexOptions.Multiline, 1000)]
    private static partial Regex TemplateLine();

    /// <summary>Language to use when the user has not chosen one: Windows' own if there is a file for it, else English.</summary>
    public static string SystemDefault()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Available().Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ? code.ToLowerInvariant() : Fallback;
    }

    public void Load(string? code)
    {
        code = string.IsNullOrWhiteSpace(code) ? SystemDefault() : code;
        _fallback = LoadMerged(Fallback);
        _strings = code.Equals(Fallback, StringComparison.OrdinalIgnoreCase) ? _fallback : LoadMerged(code);
        if (_strings.Count == 0) code = Fallback;
        _language = code;
        try { _culture = CultureInfo.GetCultureInfo(code); } catch { _culture = CultureInfo.InvariantCulture; }
        _scopes.Clear();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        Changed?.Invoke();
    }

    /// <summary>
    /// Texts of a language whose keys start with <paramref name="prefix"/> (English for the missing ones), with the
    /// user's files on top. Used by the web app, which may run in another language than the PC.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Strings(string code, string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in LoadMerged(Fallback)) if (k.StartsWith(prefix, StringComparison.Ordinal)) result[k] = v;
        if (!code.Equals(Fallback, StringComparison.OrdinalIgnoreCase))
            foreach (var (k, v) in LoadMerged(code)) if (k.StartsWith(prefix, StringComparison.Ordinal)) result[k] = v;
        return result;
    }

    private string Get(string key) =>
        (_scope.Value is { } scoped ? scoped.Strings : _strings).TryGetValue(key, out var s) ? s : _fallback.TryGetValue(key, out var f) ? f : key;

    /// <summary>The built-in texts of the language, with the user's file (if any) on top: its keys win.</summary>
    private static Dictionary<string, string> LoadMerged(string code)
    {
        var merged = ReadEmbedded(code);
        var file = Path.Combine(AppPaths.LanguagesDir, code + ".json");
        if (File.Exists(file))
            foreach (var (k, v) in ReadFile(file)) merged[k] = v;
        return merged;
    }

    private static Dictionary<string, string> ReadEmbedded(string code) =>
        EmbeddedAssets.ReadText(EmbeddedFolder, code + ".json") is { } json ? Parse(json, $"{code} (incluido)") : new(StringComparer.Ordinal);

    private static Dictionary<string, string> ReadFile(string file)
    {
        try { return Parse(File.ReadAllText(file), file); }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer el idioma {file}: {ex.Message}");
            return new(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, string> Parse(string json, string source)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(json, options);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) dict[p.Name] = p.Value.GetString()!;
        }
        catch (Exception ex) { Log.Warn($"No se pudo leer el idioma {source}: {ex.Message}"); }
        return dict;
    }
}
