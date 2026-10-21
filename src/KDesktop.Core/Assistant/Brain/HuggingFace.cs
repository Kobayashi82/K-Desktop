using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Brain;

/// <summary>A file of a Hugging Face repository (with its SHA-256 when it is a big one, stored with LFS).</summary>
public sealed record HfFile(string Path, long Size, string Sha256);

/// <summary>
/// A model of a repository: its <c>.gguf</c> (several parts when it is split; the first one is what llama-server opens)
/// or a vision projector.
/// </summary>
public sealed record HfModel(string Repo, IReadOnlyList<HfFile> Parts, bool IsProjector)
{
    public string File => Parts[0].Path;

    public string Name => System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileName(File));

    public long Size => Parts.Sum(p => p.Size);
}

/// <summary>What the user typed or an assistant suggests: a repository and, maybe, a file or a quantization («:Q4_K_M»).</summary>
public sealed record HfRef(string Repo, string? File = null, string? Quant = null)
{
    public override string ToString() => File is not null ? $"{Repo}/{File}" : Quant is not null ? $"{Repo}:{Quant}" : Repo;
}

/// <summary>
/// Hugging Face for the brain (5.0.107): reads a link or a repository name, lists its <c>.gguf</c> with their sizes and
/// turns a choice into the files K-Desktop downloads (resumable and checked, with <see cref="ModelDownloader"/>).
/// </summary>
public static partial class HuggingFace
{
    public const string Site = "https://huggingface.co/";

    /// <summary>
    /// A link or a name as typed: «https://huggingface.co/unsloth/Qwen3.5-9B-GGUF», «…/blob/main/x.gguf», «…/resolve/main/x.gguf»,
    /// «hf.co/…», «unsloth/Qwen3.5-9B-GGUF», «unsloth/Qwen3.5-9B-GGUF:Q4_K_M» or «unsloth/Qwen3.5-9B-GGUF/x.gguf». Null: not one.
    /// </summary>
    public static HfRef? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().Trim('"');
        s = SitePrefix().Replace(s, string.Empty);
        var query = s.IndexOfAny(['?', '#']);
        if (query >= 0) s = s[..query];
        s = s.Trim('/');
        if (s.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !Name().IsMatch(parts[0])) return null;
        var (owner, repo) = (parts[0], parts[1]);
        string? quant = null;
        var colon = repo.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0)
        {
            quant = repo[(colon + 1)..];
            repo = repo[..colon];
        }
        if (!Name().IsMatch(repo)) return null;
        string? file = null;
        var rest = parts.Skip(2).ToList();
        // «blob/main/…», «resolve/main/…», «tree/main/…»: the branch is not part of the file's path.
        if (rest.Count >= 2 && rest[0] is "blob" or "resolve" or "tree") rest = rest.Skip(2).ToList();
        if (rest.Count > 0 && rest[^1].EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) file = Uri.UnescapeDataString(string.Join('/', rest));
        return new HfRef($"{owner}/{repo}", file, string.IsNullOrWhiteSpace(quant) ? null : quant);
    }

    /// <summary>Where a file is downloaded from.</summary>
    public static string UrlOf(string repo, string path) =>
        $"{Site}{repo}/resolve/main/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";

    /// <summary>The page of a repository.</summary>
    public static string PageOf(string repo) => Site + repo;

    /// <summary>
    /// The <c>.gguf</c> of a repository, models first (smallest first) and then the projectors. Throws
    /// <see cref="HttpRequestException"/> (no network, not found, private or gated) with the status code when there is one.
    /// </summary>
    public static async Task<IReadOnlyList<HfModel>> ListAsync(HttpClient http, string repo, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri($"{Site}api/models/{repo}/tree/main?recursive=1"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(response.ReasonPhrase, null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var files = new List<HfFile>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "file") continue;
            var path = item.GetProperty("path").GetString() ?? string.Empty;
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
            var size = item.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
            var sha = string.Empty;
            if (item.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
            {
                if (lfs.TryGetProperty("oid", out var oid)) sha = oid.GetString() ?? string.Empty;
                if (lfs.TryGetProperty("size", out var ls) && ls.TryGetInt64(out var big)) size = big;
            }
            files.Add(new HfFile(path, size, sha));
        }
        return Group(repo, files);
    }

    /// <summary>The files as models: the parts of a split one together, without the drafters and the like.</summary>
    public static IReadOnlyList<HfModel> Group(string repo, IEnumerable<HfFile> files)
    {
        var list = files.ToList();
        var models = new List<HfModel>();
        foreach (var file in list)
        {
            var name = System.IO.Path.GetFileName(file.Path);
            if (GgufFiles.IsProjector(name))
            {
                models.Add(new HfModel(repo, [file], true));
                continue;
            }
            if (Part().Match(file.Path) is { Success: true } part)
            {
                if (part.Groups[2].Value != "00001") continue;
                var parts = list.Where(f => f.Path.StartsWith(part.Groups[1].Value, StringComparison.OrdinalIgnoreCase) && Part().IsMatch(f.Path))
                    .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
                models.Add(new HfModel(repo, parts, false));
                continue;
            }
            if (GgufFiles.IsModel(name)) models.Add(new HfModel(repo, [file], false));
        }
        return [.. models.OrderBy(m => m.IsProjector).ThenBy(m => m.Size)];
    }

    /// <summary>The model a reference names in a list: its file, its quantization (the smallest of that name), or null.</summary>
    public static HfModel? Pick(IReadOnlyList<HfModel> models, HfRef reference)
    {
        var talk = models.Where(m => !m.IsProjector).ToList();
        if (reference.File is { } file)
            return talk.FirstOrDefault(m => string.Equals(m.File, file, StringComparison.OrdinalIgnoreCase))
                ?? talk.FirstOrDefault(m => string.Equals(System.IO.Path.GetFileName(m.File), System.IO.Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
        if (reference.Quant is { } quant)
            return talk.FirstOrDefault(m => m.Name.EndsWith(quant, StringComparison.OrdinalIgnoreCase))
                ?? talk.FirstOrDefault(m => m.Name.Contains(quant, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>The projector to download with a model (as on disk: the one whose name fits best), or null.</summary>
    public static HfModel? ProjectorFor(IReadOnlyList<HfModel> models, HfModel model)
    {
        var projectors = models.Where(m => m.IsProjector).ToList();
        // F16 is the usual one (BF16 and F32 weigh the same or double and do not look better).
        var path = GgufFiles.BestProjector(model.File, [.. projectors.OrderByDescending(p => p.Name.Contains("f16", StringComparison.OrdinalIgnoreCase) && !p.Name.Contains("bf16", StringComparison.OrdinalIgnoreCase)).Select(p => p.File)]);
        return projectors.FirstOrDefault(p => p.File == path);
    }

    /// <summary>
    /// The files to download into <paramref name="folder"/> (a folder of its own: «Qwen3.5-9B-GGUF», so its projector is
    /// found next to it), and where the model will be.
    /// </summary>
    public static (IReadOnlyList<ModelFile> Files, string ModelPath) FilesOf(HfModel model, HfModel? projector, string folder)
    {
        var target = System.IO.Path.Combine(folder, model.Repo.Split('/')[1]);
        string Local(HfFile f) => System.IO.Path.Combine(target, System.IO.Path.GetFileName(f.Path));
        var files = model.Parts.Concat(projector?.Parts ?? []).Select(f => new ModelFile(UrlOf(model.Repo, f.Path), Local(f), f.Size, f.Sha256)).ToList();
        return (files, Local(model.Parts[0]));
    }

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?(?:huggingface\.co|hf\.co)/", RegexOptions.IgnoreCase)]
    private static partial Regex SitePrefix();

    [GeneratedRegex(@"^[\w.\-]+$")]
    private static partial Regex Name();

    [GeneratedRegex(@"^(.*-)(\d{5})-of-\d{5}\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex Part();
}
