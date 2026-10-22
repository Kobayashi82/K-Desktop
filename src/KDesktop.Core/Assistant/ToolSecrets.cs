using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The secrets of the JSON tools and the MCP servers (stage 9.1, plan §7.2): <c>&lt;data&gt;\Assistant\secrets.json</c>,
/// name → value encrypted with DPAPI (<see cref="Secrets"/>). A tool says <c>{{secret:ha_token}}</c> and the value is put
/// in only when the request leaves; it never reaches the model, and a result that contains it is masked. A value
/// written by hand in clear is encrypted the next time the file is read. Read again whenever the file changes;
/// thread-safe. Options → Tools edits it (stage 9.3).
/// </summary>
public sealed partial class ToolSecrets(string file)
{
    /// <summary>What a secret looks like in a result once masked.</summary>
    public const string Hidden = "•••";

    private readonly object _gate = new();
    private Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _read = DateTime.MinValue;

    public string File => file;

    /// <summary>Whether <paramref name="name"/> is a valid secret name (letters, digits, <c>_ . -</c>).</summary>
    public static bool IsName(string name) => NamePattern().IsMatch(name);

    /// <summary>The value of <paramref name="name"/>, or null when there is none (or it cannot be read on this PC).</summary>
    public string? Get(string name)
    {
        lock (_gate)
        {
            Refresh();
            return _values.GetValueOrDefault(name);
        }
    }

    /// <summary>The names kept (also those that cannot be read here).</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate)
            {
                Refresh();
                return [.. Stored().Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>Keeps <paramref name="value"/> as <paramref name="name"/> (encrypted); null or empty removes it.</summary>
    public void Set(string name, string? value)
    {
        if (!IsName(name)) throw new ArgumentException($"Nombre de secreto no válido: {name}", nameof(name));
        lock (_gate)
        {
            var stored = Stored();
            if (string.IsNullOrEmpty(value)) stored.Remove(name);
            else stored[name] = Secrets.Protect(value);
            Write(stored);
            _read = DateTime.MinValue;
        }
    }

    /// <summary><paramref name="text"/> with every secret value in it replaced by <see cref="Hidden"/>.</summary>
    public string Mask(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string[] values;
        lock (_gate)
        {
            Refresh();
            values = [.. _values.Values];
        }
        return Mask(text, values);
    }

    /// <summary><paramref name="text"/> without <paramref name="values"/> (the longest first; values under 4 characters are left: too common).</summary>
    public static string Mask(string text, IEnumerable<string> values)
    {
        foreach (var value in values.Where(v => v.Length >= 4).OrderByDescending(v => v.Length))
            text = text.Replace(value, Hidden, StringComparison.Ordinal);
        return text;
    }

    private void Refresh()
    {
        DateTime written;
        try { written = System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file) : DateTime.MinValue; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if (written == _read) return;
        _read = written;

        var stored = Stored();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clear = false;
        foreach (var (name, value) in stored.ToList())
        {
            if (Secrets.Unprotect(value) is { } plain) values[name] = plain;
            else if (!value.StartsWith("dpapi:", StringComparison.Ordinal) && value.Length > 0)
            {
                // Written by hand: kept encrypted from now on.
                values[name] = value;
                stored[name] = Secrets.Protect(value);
                clear = true;
            }
            else Log.Warn($"Asistente: el secreto «{name}» no se puede leer en este PC (hay que volver a escribirlo)");
        }
        _values = values;
        if (clear)
        {
            Write(stored);
            try { _read = System.IO.File.GetLastWriteTimeUtc(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _read = DateTime.MinValue; }
        }
    }

    private Dictionary<string, string> Stored()
    {
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!System.IO.File.Exists(file)) return stored;
            var node = JsonNode.Parse(System.IO.File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (node is JsonObject json)
                foreach (var (name, value) in json)
                    if (IsName(name) && value is JsonValue v && v.TryGetValue<string>(out var text)) stored[name] = text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Asistente: no se ha podido leer {file}: {ex.Message}");
        }
        return stored;
    }

    private void Write(Dictionary<string, string> stored)
    {
        var json = new JsonObject();
        foreach (var (name, value) in stored.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) json[name] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar {file}: {ex.Message}");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex NamePattern();
}
