using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>Why a package was turned down (the message says it to the user).</summary>
public enum PackageProblem
{
    NotAPackage,
    UnsafePath,
    TooBig,
    Missing,
    Invalid,
    ReservedId,
    Sdk,
    BadImage,
    /// <summary>The file changed between its card and the install.</summary>
    Changed,
    /// <summary>Its signature does not match its files (stage 12): changed after it was signed.</summary>
    BadSignature,
    InstallFailed,
}

/// <summary>A package that cannot be installed; the message is in the user's language.</summary>
public sealed class PackageException(PackageProblem problem, string message, Exception? inner = null) : Exception(message, inner)
{
    public PackageProblem Problem { get; } = problem;
}

/// <summary>What installing a package does, compared with what is installed.</summary>
public enum PackageAction
{
    New,
    Update,
    Reinstall,
    /// <summary>An older version than the one installed.</summary>
    Downgrade,
}

/// <summary>An installed assistant: its manifest and its folder (<c>Assistants\&lt;id&gt;\&lt;version&gt;</c>).</summary>
public sealed record InstalledAssistant(AssistantManifest Manifest, string Folder)
{
    public string Id => Manifest.Id;

    public string Version => Manifest.Version;
}

/// <summary>A checked package, ready for its card and for <see cref="AssistantPackages.Install"/>.</summary>
public sealed class PackageInfo
{
    public required string File { get; init; }

    public required AssistantManifest Manifest { get; init; }

    public required PackageAction Action { get; init; }

    /// <summary>The version installed now (null when it is new).</summary>
    public InstalledAssistant? Installed { get; init; }

    /// <summary>The icon and the preview image (PNG or JPEG bytes), if the package has them.</summary>
    public byte[]? Icon { get; init; }

    public byte[]? Preview { get; init; }

    /// <summary>Who signed it (stage 12): «verificado» with a key K-Desktop trusts.</summary>
    public SignatureInfo Signature { get; init; } = SignatureInfo.None;

    /// <summary>
    /// The version installed was signed and this one is not by the same key (unsigned, or by another one): an update
    /// that may not come from whoever made the assistant.
    /// </summary>
    public bool SignerChanged { get; init; }

    /// <summary>Size once installed, in bytes.</summary>
    public required long Size { get; init; }

    public required int Files { get; init; }

    /// <summary>What it can do: its gestures plus the two every assistant has.</summary>
    public IReadOnlyList<string> Gestures => [.. Manifest.Traits.Gestures.Union(["communicate", "react"])];

    /// <summary>SHA-256 of the file when it was checked: installing it again checks that it did not change.</summary>
    internal byte[] Hash { get; init; } = [];
}

/// <summary>
/// The package manager (stage 1.7, §11.7): checks a <c>.kai</c> without ever loading its code (K-Desktop is
/// elevated), installs it in <c>Assistants\&lt;id&gt;\&lt;version&gt;</c>, and removes it. A folder that cannot be
/// deleted because the assistant process still holds its files gets a mark and goes in <see cref="CleanUp"/>.
/// </summary>
public static partial class AssistantPackages
{
    public const string Extension = ".kai";

    internal const long MaxSize = 512L << 20;
    internal const int MaxFiles = 10_000;
    private const long MaxJson = 1L << 20;
    private const long MaxImage = 8L << 20;

    /// <summary>A version folder with this file is waiting to be deleted and does not count.</summary>
    private const string DeleteMark = ".delete";

    private const string TempPrefix = ".tmp-";

    private static readonly object Gate = new();

    /// <summary>Version of the SDK this K-Desktop gives assistants (major and minor).</summary>
    public static Version SdkVersion { get; } = typeof(IAssistant).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor) : new Version(1, 0);

    /// <summary>Major version of the SDK this K-Desktop gives assistants.</summary>
    public static int SdkMajor => SdkVersion.Major;

    /// <summary>Opens and checks a package. Throws <see cref="PackageException"/> with what is wrong.</summary>
    public static PackageInfo Inspect(string file)
    {
        var name = Path.GetFileName(file);
        try
        {
            var hash = HashFile(file);
            using var zip = ZipFile.OpenRead(file);
            var entries = Entries(zip, out var size);

            var manifestJson = ReadText(entries, AssistantManifest.FileName)
                               ?? throw Fail(PackageProblem.Missing, "assistantPkg.missing", AssistantManifest.FileName);
            CheckSchema("assistant", AssistantManifest.FileName, manifestJson);
            AssistantManifest manifest;
            try { manifest = AssistantManifest.Parse(manifestJson); }
            catch (ManifestException ex) { throw Fail(PackageProblem.Invalid, "assistantPkg.invalid", AssistantManifest.FileName, ex.Message); }

            if (AssistantPaths.Reserved.Contains(manifest.Id)) throw Fail(PackageProblem.ReservedId, "assistantPkg.reservedId", manifest.Id);
            if (manifest.SdkMajor != SdkMajor) throw Fail(PackageProblem.Sdk, "assistantPkg.sdk", manifest.Name, manifest.Sdk, SdkMajor);
            if (!manifest.RunsOn(SdkVersion)) throw Fail(PackageProblem.Sdk, "assistantPkg.sdkNewer", manifest.Name, manifest.Sdk, SdkVersion.ToString(2));
            var dll = manifest.EntryParts.Assembly;
            if (!entries.ContainsKey(dll)) throw Fail(PackageProblem.Missing, "assistantPkg.missing", dll);

            var icon = Image(entries, manifest.Icon);
            var preview = Image(entries, manifest.Preview);
            foreach (var extra in manifest.Extras)
                if (extra.Icon is { } extraIcon && !extraIcon.StartsWith("kd:", StringComparison.Ordinal)) Image(entries, extraIcon);

            // settings.json and panel.json are optional unless the manifest names them.
            CheckFile(entries, "settings", manifest.SettingsFile, required: manifest.Settings is not null);
            CheckFile(entries, "panel", manifest.PanelFile, required: manifest.Panel is not null);
            if (manifest.Voice is { } voice) CheckFile(entries, "voice", voice, required: true);
            if (manifest.Persona is { } persona) CheckPersona(entries, persona);

            var signature = PackageSignature.Check(entries);
            if (signature.State == SignatureState.Broken) throw Fail(PackageProblem.BadSignature, "assistantPkg.badSignature", manifest.Name);

            var installed = Find(manifest.Id);
            var before = installed is null ? SignatureInfo.None : PackageSignature.OfInstalled(installed.Folder);
            return new PackageInfo
            {
                File = Path.GetFullPath(file),
                Manifest = manifest,
                Action = installed is null ? PackageAction.New : CompareVersions(manifest.Version, installed.Version) switch
                {
                    > 0 => PackageAction.Update,
                    0 => PackageAction.Reinstall,
                    _ => PackageAction.Downgrade,
                },
                Installed = installed,
                Icon = icon,
                Preview = preview,
                Signature = signature,
                SignerChanged = before.IsSigned && before.Fingerprint != signature.Fingerprint,
                Size = size,
                Files = entries.Count,
                Hash = hash,
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new PackageException(PackageProblem.NotAPackage, Loc.T("assistantPkg.notPackage", name), ex);
        }
    }

    /// <summary>
    /// Installs a checked package: extracted apart and then moved into place, so a failure leaves nothing half done.
    /// Other versions of the same assistant are removed; its data and settings are kept.
    /// </summary>
    public static InstalledAssistant Install(PackageInfo package)
    {
        var manifest = package.Manifest;
        lock (Gate)
        {
            Directory.CreateDirectory(AssistantPaths.Root);
            CleanUpLocked();
            var temp = Path.Combine(AssistantPaths.Root, TempPrefix + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                if (!HashFile(package.File).AsSpan().SequenceEqual(package.Hash))
                    throw Fail(PackageProblem.Changed, "assistantPkg.changed");
                using (var zip = ZipFile.OpenRead(package.File))
                    foreach (var (path, entry) in Entries(zip, out _))
                    {
                        var target = Path.Combine(temp, path.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        using var source = entry.Open();
                        using var output = File.Create(target);
                        CopyLimited(source, output, entry.Length, path);
                    }

                var idFolder = Path.Combine(AssistantPaths.Root, manifest.Id);
                Directory.CreateDirectory(idFolder);
                var folder = FreeFolder(idFolder, manifest.Version);
                Directory.Move(temp, folder);
                foreach (var old in Directory.EnumerateDirectories(idFolder).Where(d => !d.Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList())
                    Remove(old);
                Log.Info($"Asistentes: «{manifest.Id}» {manifest.Version} instalado en {folder}");
                return new InstalledAssistant(AssistantManifest.Load(folder), folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ManifestException)
            {
                TryDelete(temp);
                Log.Error($"Asistentes: no se ha podido instalar «{manifest.Id}»", ex);
                throw new PackageException(PackageProblem.InstallFailed, Loc.T("assistantPkg.installFailed", manifest.Name, ex.Message), ex);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
    }

    /// <summary>
    /// Removes an assistant. Its data (progress, statistics) and its settings stay unless <paramref name="keepData"/>
    /// is false. Unload it first: files still in use are deleted later.
    /// </summary>
    public static void Uninstall(string id, bool keepData)
    {
        if (!AssistantManifest.IsValidId(id) || AssistantPaths.Reserved.Contains(id)) return;
        lock (Gate)
        {
            var idFolder = Path.Combine(AssistantPaths.Root, id);
            if (Directory.Exists(idFolder))
            {
                foreach (var version in Directory.EnumerateDirectories(idFolder).ToList()) Remove(version);
                DeleteIfEmpty(idFolder);
            }
            if (!keepData)
            {
                TryDelete(AssistantPaths.Data(id));
                try { File.Delete(AssistantPaths.SettingsValues(id)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Asistentes: no se han podido borrar los ajustes de «{id}» ({ex.Message})"); }
            }
            Log.Info($"Asistentes: «{id}» quitado{(keepData ? " (se conservan sus datos)" : "")}");
        }
    }

    /// <summary>The installed assistants, by id.</summary>
    public static IReadOnlyList<InstalledAssistant> Installed()
    {
        if (!Directory.Exists(AssistantPaths.Root)) return [];
        return [.. Directory.EnumerateDirectories(AssistantPaths.Root)
            .Select(Path.GetFileName)
            .Where(n => n is not null && AssistantManifest.IsValidId(n) && !AssistantPaths.Reserved.Contains(n))
            .Select(n => Find(n!))
            .OfType<InstalledAssistant>()
            .OrderBy(a => a.Id, StringComparer.Ordinal)];
    }

    /// <summary>The installed version of an assistant (the newest, if a removal is still pending), or null.</summary>
    public static InstalledAssistant? Find(string id)
    {
        if (!AssistantManifest.IsValidId(id) || AssistantPaths.Reserved.Contains(id)) return null;
        var idFolder = Path.Combine(AssistantPaths.Root, id);
        if (!Directory.Exists(idFolder)) return null;
        InstalledAssistant? best = null;
        foreach (var folder in Directory.EnumerateDirectories(idFolder))
        {
            if (File.Exists(Path.Combine(folder, DeleteMark)) || !File.Exists(Path.Combine(folder, AssistantManifest.FileName))) continue;
            try
            {
                var manifest = AssistantManifest.Load(folder);
                if (manifest.Id != id) continue;
                if (best is null || CompareVersions(manifest.Version, best.Version) > 0) best = new InstalledAssistant(manifest, folder);
            }
            catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Asistentes: se ignora {folder} ({ex.Message})");
            }
        }
        return best;
    }

    /// <summary>Deletes what was left for later: versions marked for deletion and unfinished installs.</summary>
    public static void CleanUp()
    {
        lock (Gate) CleanUpLocked();
    }

    private static void CleanUpLocked()
    {
        if (!Directory.Exists(AssistantPaths.Root)) return;
        foreach (var folder in Directory.EnumerateDirectories(AssistantPaths.Root).ToList())
        {
            var name = Path.GetFileName(folder);
            if (name.StartsWith(TempPrefix, StringComparison.Ordinal))
            {
                TryDelete(folder);
                continue;
            }
            if (!AssistantManifest.IsValidId(name) || AssistantPaths.Reserved.Contains(name)) continue;
            foreach (var version in Directory.EnumerateDirectories(folder).Where(v => File.Exists(Path.Combine(v, DeleteMark))).ToList())
                TryDelete(version);
            DeleteIfEmpty(folder);
        }
    }

    /// <summary>
    /// Orders two versions «1.2.0», «1.2.0-beta.1»: by number, and with the same number a pre-release goes before
    /// the release.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        static (Version Number, string Tag) Split(string v)
        {
            var parts = v.Split('-', 2);
            return (System.Version.TryParse(parts[0], out var n) ? n : new Version(0, 0, 0), parts.Length > 1 ? parts[1] : "");
        }

        var (na, ta) = Split(a);
        var (nb, tb) = Split(b);
        var byNumber = na.CompareTo(nb);
        if (byNumber != 0) return byNumber;
        if (ta.Length == 0 || tb.Length == 0) return tb.Length.CompareTo(ta.Length); // the release wins
        return string.CompareOrdinal(ta, tb);
    }

    // ── reading the package ─────────────────────────────────────────────

    /// <summary>The files of the package by path («lib/X.dll»), after checking every path and the sizes.</summary>
    private static Dictionary<string, ZipArchiveEntry> Entries(ZipArchive zip, out long size)
    {
        if (zip.Entries.Count > MaxFiles) throw Fail(PackageProblem.TooBig, "assistantPkg.tooBig", zip.Entries.Count);
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        size = 0;
        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            var isFolder = path.EndsWith('/');
            if (!IsSafePath(isFolder ? path.TrimEnd('/') : path)) throw Fail(PackageProblem.UnsafePath, "assistantPkg.unsafePath", entry.FullName);
            if (isFolder) continue;
            if (!result.TryAdd(path, entry)) throw Fail(PackageProblem.UnsafePath, "assistantPkg.unsafePath", entry.FullName);
            size += entry.Length;
            // Over the limit, or a small file that would unpack into a huge one.
            if (size > MaxSize || (entry.Length > (1L << 20) && entry.Length > entry.CompressedLength * 200))
                throw Fail(PackageProblem.TooBig, "assistantPkg.tooBig", entry.FullName);
        }
        return result;
    }

    /// <summary>A relative path that stays in its folder and that Windows can write as it is.</summary>
    public static bool IsSafePath(string path)
    {
        if (path.Length is 0 or > 200 || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal)) return false;
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || DeviceName().IsMatch(part)) return false;
        }
        return true;
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceName();

    private static string? ReadText(Dictionary<string, ZipArchiveEntry> entries, string path) =>
        ReadBytes(entries, path, MaxJson) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    private static byte[]? ReadBytes(Dictionary<string, ZipArchiveEntry> entries, string path, long max)
    {
        if (!entries.TryGetValue(path, out var entry)) return null;
        if (entry.Length > max) throw Fail(PackageProblem.TooBig, "assistantPkg.tooBig", path);
        using var source = entry.Open();
        using var buffer = new MemoryStream((int)entry.Length);
        CopyLimited(source, buffer, entry.Length, path);
        return buffer.ToArray();
    }

    /// <summary>Copies an entry, never more than its header says (a lying header is a broken package).</summary>
    private static void CopyLimited(Stream source, Stream target, long length, string path)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > length) throw Fail(PackageProblem.TooBig, "assistantPkg.tooBig", path);
            target.Write(buffer, 0, read);
        }
    }

    private static void CheckSchema(string schema, string path, string json)
    {
        var errors = PackageSchemas.Validate(schema, json);
        if (errors.Count > 0) throw Fail(PackageProblem.Invalid, "assistantPkg.invalid", path, string.Join("; ", errors.Take(3)));
    }

    private static void CheckFile(Dictionary<string, ZipArchiveEntry> entries, string schema, string path, bool required)
    {
        if (ReadText(entries, path) is { } json) CheckSchema(schema, path, json);
        else if (required) throw Fail(PackageProblem.Missing, "assistantPkg.missing", path);
    }

    /// <summary>The persona: one file, or one per language when the path has «{lang}» (at least one must be there).</summary>
    private static void CheckPersona(Dictionary<string, ZipArchiveEntry> entries, string persona)
    {
        if (!persona.Contains("{lang}", StringComparison.Ordinal))
        {
            CheckFile(entries, "persona", persona, required: true);
            return;
        }
        var pattern = new Regex("^" + Regex.Escape(persona).Replace(@"\{lang}", "[a-z]{2}(-[A-Z]{2})?", StringComparison.Ordinal) + "$");
        var files = entries.Keys.Where(k => pattern.IsMatch(k)).ToList();
        if (files.Count == 0) throw Fail(PackageProblem.Missing, "assistantPkg.missing", persona);
        foreach (var file in files) CheckFile(entries, "persona", file, required: true);
    }

    /// <summary>An image of the package (PNG or JPEG), or null when there is none.</summary>
    private static byte[]? Image(Dictionary<string, ZipArchiveEntry> entries, string? path)
    {
        if (path is null) return null;
        var bytes = ReadBytes(entries, path, MaxImage) ?? throw Fail(PackageProblem.Missing, "assistantPkg.missing", path);
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];
        if (!bytes.AsSpan().StartsWith(png) && !bytes.AsSpan().StartsWith(jpeg)) throw Fail(PackageProblem.BadImage, "assistantPkg.badImage", path);
        return bytes;
    }

    private static byte[] HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return SHA256.HashData(stream);
    }

    // ── folders ─────────────────────────────────────────────────────────

    /// <summary>
    /// <c>&lt;id&gt;\&lt;version&gt;</c>, emptied if it was there; when its files are still in use (reinstalling the
    /// loaded version), the next free name: the folder name is not what says the version, the manifest is.
    /// </summary>
    private static string FreeFolder(string idFolder, string version)
    {
        var folder = Path.Combine(idFolder, version);
        for (var n = 2; Directory.Exists(folder); n++)
        {
            Remove(folder);
            if (!Directory.Exists(folder)) break;
            folder = Path.Combine(idFolder, $"{version}_{n}");
        }
        return folder;
    }

    /// <summary>Deletes a version folder, or marks it to be deleted later if its files are in use.</summary>
    private static void Remove(string folder)
    {
        if (TryDelete(folder)) return;
        try
        {
            File.WriteAllText(Path.Combine(folder, DeleteMark), string.Empty);
            Log.Info($"Asistentes: {folder} está en uso; se borrará más tarde");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistentes: no se ha podido marcar {folder} para borrarlo ({ex.Message})");
        }
    }

    private static bool TryDelete(string folder)
    {
        if (!Directory.Exists(folder)) return true;
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next clean-up */ }
    }

    private static PackageException Fail(PackageProblem problem, string key, params object?[] args) => new(problem, Loc.T(key, args));
}
