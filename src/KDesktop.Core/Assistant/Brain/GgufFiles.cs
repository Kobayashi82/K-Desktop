using System.Text.RegularExpressions;

namespace KDesktop.Core.Assistant.Brain;

/// <summary>A <c>.gguf</c> model of this PC: its size, its vision projector (a <c>mmproj</c> next to it) and the llama-server that opens it.</summary>
public sealed record LocalModel(string Path, long Size, string? Projector, long ProjectorSize, string? Server);

/// <summary>
/// The <c>.gguf</c> files of the brain (5.0.107): which are models and which are vision projectors, the projector of each
/// model, and which llama-server opens each one.
/// </summary>
public static partial class GgufFiles
{
    /// <summary>How deep the folders are looked into («Models\Qwen 3.5 9B\x.gguf», a demo's «models\bonsai2-gguf\27B\x.gguf»).</summary>
    public const int Depth = 4;

    /// <summary>At most this many models are listed.</summary>
    public const int MaxModels = 120;

    private static readonly string[] NotModels = ["mmproj", "dspark", "dflash", "kv-bias", "draft", "embed", "vocab", "reranker", "imatrix"];

    /// <summary>Whether a <c>.gguf</c> is a model one can talk to (not a projector, a drafter, the second part of a split one…).</summary>
    public static bool IsModel(string fileName) =>
        fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
        && !NotModels.Any(n => fileName.Contains(n, StringComparison.OrdinalIgnoreCase))
        && !LaterPart().IsMatch(fileName);

    /// <summary>Whether a <c>.gguf</c> is a vision projector.</summary>
    public static bool IsProjector(string fileName) =>
        fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && fileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The projector of a model among those next to it: the only one, or the one whose name shares the most words with the
    /// model's («Qwen3.5-9B-uncensored-heretic-Q4_K_M» → «Qwen3.5-9B-uncensored-heretic-mmproj-BF16»), with fewer words of
    /// its own on a tie, and then the first given. Null: none.
    /// </summary>
    public static string? BestProjector(string model, IReadOnlyList<string> projectors)
    {
        if (projectors.Count == 0) return null;
        if (projectors.Count == 1) return projectors[0];
        var words = Words(model);
        return projectors
            .Select(p => (Path: p, Words: Words(p)))
            .OrderByDescending(p => p.Words.Count(words.Contains))
            .ThenBy(p => p.Words.Count(w => !words.Contains(w)))
            .First().Path;
    }

    private static HashSet<string> Words(string path) =>
        [.. WordSplit().Split(System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant()).Where(w => w.Length > 0 && w != "mmproj" && w != "gguf")];

    /// <summary>
    /// The models in <paramref name="folders"/> (and the <paramref name="chosen"/> ones, wherever they are), each with its
    /// projector and its server (<see cref="ServerFor"/>), by name. Folders that are not there or cannot be read are skipped.
    /// </summary>
    public static IReadOnlyList<LocalModel> Find(IEnumerable<string> folders, IEnumerable<string> chosen, IReadOnlyList<string> servers)
    {
        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string file)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Exists) files.TryAdd(info.FullName, info);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A path that is not one.
            }
        }
        foreach (var file in chosen.Where(ModelRefs.IsLocal)) Add(file);
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = Depth, IgnoreInaccessible = true };
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.gguf", options))
                {
                    if (files.Count >= MaxModels * 3) break;
                    Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A folder that cannot be read.
            }
        }

        var models = new List<LocalModel>();
        foreach (var folder in files.Values.GroupBy(f => f.DirectoryName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var projectors = Directory.Exists(folder.Key)
                ? SafeFiles(folder.Key).Where(p => IsProjector(System.IO.Path.GetFileName(p))).ToList()
                : [];
            foreach (var file in folder.Where(f => IsModel(f.Name) || chosen.Any(c => ModelRefs.Same(c, f.FullName))))
            {
                var projector = BestProjector(file.FullName, projectors);
                // A model split in parts opens from the first one and weighs all of them.
                var size = FirstPart().Match(file.Name) is { Success: true } split
                    ? SafeFiles(folder.Key).Where(p => System.IO.Path.GetFileName(p).StartsWith(split.Groups[1].Value, StringComparison.OrdinalIgnoreCase)).Sum(SafeLength)
                    : file.Length;
                models.Add(new LocalModel(file.FullName, size, projector, projector is null ? 0 : SafeLength(projector), ServerFor(file.FullName, servers)));
            }
        }
        return [.. models.OrderBy(m => System.IO.Path.GetFileName(m.Path), StringComparer.OrdinalIgnoreCase).Take(MaxModels)];
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try { return Directory.GetFiles(folder, "*.gguf"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// The llama-server that opens a model: the one whose home (its folder and the two above it, never a whole drive or
    /// the user's folder) is the closest folder above the model —Bonsai's models, next to Bonsai's server, open with it—;
    /// otherwise the first one, the usual. Null without servers.
    /// </summary>
    public static string? ServerFor(string model, IReadOnlyList<string> servers)
    {
        if (servers.Count == 0) return null;
        string? best = null;
        var bestDepth = -1;
        foreach (var server in servers)
        {
            foreach (var home in Homes(server))
            {
                if (!IsInside(model, home)) continue;
                var depth = home.TrimEnd('\\').Count(c => c == '\\');
                if (depth > bestDepth) (best, bestDepth) = (server, depth);
            }
        }
        return best ?? servers[0];
    }

    /// <summary>A server's folder and the two above it, without a whole drive or the user's folder.</summary>
    public static IEnumerable<string> Homes(string server)
    {
        string? folder;
        try { folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(server)); }
        catch (ArgumentException) { yield break; }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var up = 0; up < 3 && folder is not null; up++, folder = System.IO.Path.GetDirectoryName(folder))
        {
            if (string.Equals(System.IO.Path.GetPathRoot(folder)?.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) yield break;
            if (string.Equals(folder.TrimEnd('\\'), profile.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) yield break;
            yield return folder;
        }
    }

    /// <summary>
    /// A folder of models next to a llama-server (a «models» folder in its folder or the two above it), to look in when
    /// that server is added; null: none.
    /// </summary>
    public static string? ModelsFolderNear(string server)
    {
        foreach (var home in Homes(server))
        {
            try
            {
                var found = Directory.EnumerateDirectories(home).FirstOrDefault(d => string.Equals(System.IO.Path.GetFileName(d), "models", StringComparison.OrdinalIgnoreCase));
                if (found is not null) return found;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read.
            }
        }
        return null;
    }

    public static bool IsInside(string path, string folder)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var root = System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"-0000[2-9]-of-\d+\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex LaterPart();

    [GeneratedRegex(@"^(.*-)00001-of-\d+\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex FirstPart();

    [GeneratedRegex(@"[-_.\s]+")]
    private static partial Regex WordSplit();
}
