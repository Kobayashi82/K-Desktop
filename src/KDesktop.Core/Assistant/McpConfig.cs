using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Core.Assistant;

/// <summary>
/// One MCP server of <c>Assistant\mcp.json</c> (stage 9.2, plan §7.3), in the format other MCP clients use
/// (<c>mcpServers</c>: Claude Desktop, mcpo…): a program spoken to through its standard input and output
/// (<c>command</c>, <c>args</c>, <c>env</c>, <c>cwd</c>) or an address (<c>url</c>, <c>headers</c>, <c>type</c> <c>sse</c>
/// for the old transport). K-Desktop adds <c>disabled</c>, <c>timeout</c> (seconds per call) and <c>idleMinutes</c>
/// (stopped after that long unused). Values may say <c>{{secret:nombre}}</c>.
/// </summary>
/// <param name="Raw">Its object as written (kept whole, so what K-Desktop does not know survives a save).</param>
public sealed record McpServerEntry(string Id, JsonObject Raw)
{
    public string? Command => Text(Raw["command"]);

    public IReadOnlyList<string> Args => Raw["args"] is JsonArray args ? [.. args.Select(Text).OfType<string>()] : [];

    public IReadOnlyDictionary<string, string> Env => Strings(Raw["env"]);

    public string? Cwd => Text(Raw["cwd"]);

    public string? Url => Text(Raw["url"]);

    public IReadOnlyDictionary<string, string> Headers => Strings(Raw["headers"]);

    /// <summary><c>stdio</c>, <c>http</c> (streamable HTTP, or SSE when the server only has that) or <c>sse</c>.</summary>
    public string Transport => Url is null ? "stdio" : string.Equals(Text(Raw["type"]), "sse", StringComparison.OrdinalIgnoreCase) ? "sse" : "http";

    public bool Disabled => Raw["disabled"] is JsonValue v && v.TryGetValue<bool>(out var off) && off;

    /// <summary>How long a call may take.</summary>
    public TimeSpan CallTimeout => TimeSpan.FromSeconds(Math.Clamp(Number(Raw["timeout"]) ?? 60, 5, 600));

    /// <summary>How long it stays running unused.</summary>
    public TimeSpan Idle => TimeSpan.FromMinutes(Math.Clamp(Number(Raw["idleMinutes"]) ?? 5, 0.5, 240));

    /// <summary>What it is, in a few words: the program and its first arguments, or the address.</summary>
    public string Summary => Url ?? string.Join(' ', new[] { Command ?? string.Empty }.Concat(Args.Take(3))).Trim();

    /// <summary>
    /// Changes when anything that makes it what it is changes (not <c>disabled</c>, <c>timeout</c> or <c>idleMinutes</c>):
    /// the tools listed before are still its tools while this stays the same.
    /// </summary>
    public string Key
    {
        get
        {
            var copy = Raw.DeepClone().AsObject();
            copy.Remove("disabled");
            copy.Remove("timeout");
            copy.Remove("idleMinutes");
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(copy.ToJsonString())))[..16];
        }
    }

    /// <summary>The secrets it names (<c>{{secret:nombre}}</c> anywhere in it).</summary>
    public IReadOnlyList<string> SecretNames => [.. McpConfig.SecretsIn(Raw.ToJsonString()).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var n) ? n : null;

    private static Dictionary<string, string> Strings(JsonNode? node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject obj)
            foreach (var (key, value) in obj)
                if (Text(value) is { } s) result[key] = s;
                else if (value is not null) result[key] = value.ToJsonString();
        return result;
    }
}

/// <summary>A server another program had (mcpo's <c>config.json</c>…), as it would come in.</summary>
/// <param name="Entry">Its entry with the secrets already as <c>{{secret:…}}</c>.</param>
/// <param name="Secrets">The secrets taken out of it (name → value), to keep encrypted.</param>
/// <param name="Overlap">Why it is not worth it (the assistants already do that), or null.</param>
/// <param name="Exists">There is already a server with that name.</param>
public sealed record McpImportCandidate(McpServerEntry Entry, IReadOnlyDictionary<string, string> Secrets, string? Overlap, bool Exists);

/// <summary>Reading, writing and importing <c>Assistant\mcp.json</c>.</summary>
public static partial class McpConfig
{
    /// <summary>A server's name: letters, digits, <c>_ . -</c>.</summary>
    public static bool IsId(string id) => IdPattern().IsMatch(id);

    /// <summary>Its servers, in the order written; <paramref name="errors"/> says what is wrong (empty: all fine).</summary>
    public static IReadOnlyList<McpServerEntry> Parse(string json, out IReadOnlyList<string> errors)
    {
        errors = PackageSchemas.Validate("mcp", json);
        if (errors.Count > 0) return [];
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return root?["mcpServers"] is JsonObject servers
            ? [.. servers.Where(p => p.Value is JsonObject).Select(p => new McpServerEntry(p.Key, p.Value!.DeepClone().AsObject()))]
            : [];
    }

    /// <summary>The servers of <paramref name="file"/> (none when it does not exist).</summary>
    public static IReadOnlyList<McpServerEntry> Read(string file, out IReadOnlyList<string> errors)
    {
        errors = [];
        if (!File.Exists(file)) return [];
        string text;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors = [ex.Message];
            return [];
        }
        return Parse(text, out errors);
    }

    /// <summary>Writes <paramref name="servers"/> as <paramref name="file"/> (whole).</summary>
    public static void Write(string file, IEnumerable<McpServerEntry> servers)
    {
        var all = new JsonObject();
        foreach (var server in servers) all[server.Id] = server.Raw.DeepClone();
        var json = new JsonObject { ["mcpServers"] = all }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, json);
    }

    /// <summary>
    /// The servers of another program's file (<c>mcpServers</c>): each with its secret values (variables and headers
    /// whose name says token, key, secret, password… and an Authorization header) taken out as
    /// <c>{{secret:&lt;servidor&gt;.&lt;NOMBRE&gt;}}</c>, and whether the assistants already do what it does.
    /// </summary>
    /// <param name="existing">The ids already in <c>mcp.json</c>.</param>
    public static IReadOnlyList<McpImportCandidate> Import(string json, IReadOnlyCollection<string> existing)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException) { return []; }
        if ((root?["mcpServers"] ?? root?["servers"]) is not JsonObject servers) return [];

        var result = new List<McpImportCandidate>();
        foreach (var (name, value) in servers)
        {
            if (value is not JsonObject original || !IsId(name)) continue;
            var raw = new JsonObject();
            foreach (var (key, field) in original)
            {
                // mcpo's «streamable_http»/«streamable-http» are the streamable HTTP of the spec: what «url» alone means.
                if (key == "type" && field is JsonValue t && t.TryGetValue<string>(out var type) && type != "sse") continue;
                raw[key] = field?.DeepClone();
            }
            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TakeSecrets(raw["env"] as JsonObject, name, secrets, header: false);
            TakeSecrets(raw["headers"] as JsonObject, name, secrets, header: true);
            var entry = new McpServerEntry(name, raw);
            result.Add(new McpImportCandidate(entry, secrets, OverlapOf(entry), existing.Contains(name, StringComparer.OrdinalIgnoreCase)));
        }
        return result;
    }

    private static void TakeSecrets(JsonObject? values, string server, Dictionary<string, string> secrets, bool header)
    {
        if (values is null) return;
        foreach (var (key, value) in values.ToList())
        {
            if (value is not JsonValue v || !v.TryGetValue<string>(out var text) || text.Length == 0 || text.Contains("{{secret:", StringComparison.Ordinal)) continue;
            if (!IsSecretName(key) && !(header && key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))) continue;
            var name = $"{server}.{key}";
            if (!ToolSecrets.IsName(name)) continue;
            secrets[name] = text;
            values[key] = $"{{{{secret:{name}}}}}";
        }
    }

    /// <summary>Whether a variable's name says it holds a secret (TELEGRAM_TOKEN, FT_CLIENT_SECRET, API_KEY, CHAT_ID…).</summary>
    public static bool IsSecretName(string name) => SecretPattern().IsMatch(name);

    /// <summary>The secrets <paramref name="text"/> names as <c>{{secret:…}}</c>.</summary>
    public static IEnumerable<string> SecretsIn(string text) => SecretUse().Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>
    /// What a server would do that the assistants already do (files, the screen, the clipboard, memory, the time, the
    /// browser, the mouse and keyboard), as a key of the texts, or null.
    /// </summary>
    public static string? OverlapOf(McpServerEntry entry)
    {
        var what = (entry.Id + " " + entry.Command + " " + string.Join(' ', entry.Args)).ToLowerInvariant();
        foreach (var (words, key) in Overlaps)
            if (words.Any(w => what.Contains(w, StringComparison.Ordinal))) return key;
        return null;
    }

    private static readonly (string[] Words, string Key)[] Overlaps =
    [
        (["kdesktop_mcp", "kdesktop-mcp"], "mcpImport.overlap.kdesktop"),
        (["server-filesystem", "desktop-commander"], "mcpImport.overlap.files"),
        (["windows-mcp", "computer-control-mcp", "chrome-devtools-mcp"], "mcpImport.overlap.screen"),
        (["clipboard"], "mcpImport.overlap.clipboard"),
        (["server-memory"], "mcpImport.overlap.memory"),
        (["mcp-server-time"], "mcpImport.overlap.time"),
    ];

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,40}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"(TOKEN|KEY|SECRET|PASSWORD|PASSWD|PWD|PASS\b|CREDENTIAL|CLIENT_ID|CHAT_ID|AUTH)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"\{\{\s*secret:([^{}\s]+)\s*\}\}")]
    private static partial Regex SecretUse();
}
