using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Updates;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Error }

/// <summary>A newer version described by the update manifest.</summary>
public sealed record UpdateInfo(Version Version, Uri Download, string? Sha256, long Size, string? Notes, DateTime? Date);

/// <summary>
/// Checks a JSON manifest for a newer version, downloads and verifies it, and swaps the running exe for it.
/// The manifest (e.g. https://kobayashi82.net/kdesktop/latest.json):
/// <code>
/// { "version": "5.0.1", "date": "2026-10-20", "url": "KDesktop-5.0.1.exe", "sha256": "…", "size": 177000000,
///   "notes": { "es": "…", "en": "…" } }
/// </code>
/// "url" may be relative to the manifest; "notes" may also be a plain string. tools\release\make-update.ps1 writes it.
/// Installing needs the single-file portable build: the new exe replaces the running one (renamed to *.old, which a
/// running exe allows) and starts with <c>--after-update &lt;pid&gt;</c>, waiting for this process to leave.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string AfterUpdateArgument = "--after-update";

    private readonly HttpClient _http;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _download;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"K-Desktop/{CurrentVersion.ToString(3)}");
    }

    /// <summary>This build's version (major.minor.patch).</summary>
    public static Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(5, 0, 0));

    /// <summary>
    /// Only the single-file portable exe can replace itself (a framework-dependent build is many files): there the
    /// assemblies have no location on disk.
    /// </summary>
    public static bool CanInstall => string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location);

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public UpdateInfo? Available { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastCheck { get; private set; }

    /// <summary>Download progress, 0…1.</summary>
    public double Progress { get; private set; }

    /// <summary>The verified new exe, once downloaded.</summary>
    public string? ReadyFile { get; private set; }

    /// <summary>Raised (any thread) whenever the state, progress or result changes.</summary>
    public event Action? Changed;

    private void Set(UpdateState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    // ── check ────────────────────────────────────────────────────────────

    /// <summary>Reads the manifest. Returns the newer version, or null when this one is the latest (or it failed).</summary>
    public async Task<UpdateInfo?> CheckAsync(string manifestUrl, CancellationToken token = default)
    {
        if (State is UpdateState.Downloading) return Available;
        if (State is UpdateState.Ready && Available is not null) return Available;
        Set(UpdateState.Checking);
        try
        {
            if (!Uri.TryCreate(manifestUrl.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                throw new InvalidOperationException(Loc.T("updates.badUrl"));
            // No caches in between (proxies, CDN): always the current manifest.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var info = Parse(json, url);
            LastCheck = DateTime.Now;
            Log.Info($"Actualizaciones: última {info.Version}, esta {CurrentVersion}");
            if (info.Version > CurrentVersion)
            {
                Available = info;
                Set(UpdateState.Available);
                return info;
            }
            Available = null;
            Set(UpdateState.UpToDate);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            LastCheck = DateTime.Now;
            Log.Warn($"No se pudo comprobar si hay actualizaciones ({manifestUrl}): {ex.Message}");
            Set(UpdateState.Error, Describe(ex));
            return null;
        }
    }

    /// <summary>Reads the manifest JSON (public for tests).</summary>
    public static UpdateInfo Parse(string json, Uri manifestUrl)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var versionText = root.GetProperty("version").GetString() ?? throw new FormatException("version");
        var version = Normalize(Version.Parse(versionText.TrimStart('v', 'V')));
        var link = root.GetProperty("url").GetString() ?? throw new FormatException("url");
        var download = new Uri(manifestUrl, link);
        var sha = root.TryGetProperty("sha256", out var s) ? s.GetString()?.Trim().ToLowerInvariant() : null;
        var size = root.TryGetProperty("size", out var z) && z.TryGetInt64(out var n) ? n : 0;
        DateTime? date = root.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
        return new UpdateInfo(version, download, string.IsNullOrEmpty(sha) ? null : sha, size, Notes(root), date);
    }

    /// <summary>"notes" as a string, or the text in the UI language (else English, else the first one).</summary>
    private static string? Notes(JsonElement root)
    {
        if (!root.TryGetProperty("notes", out var notes)) return null;
        if (notes.ValueKind == JsonValueKind.String) return notes.GetString();
        if (notes.ValueKind != JsonValueKind.Object) return null;
        string? first = null, english = null;
        foreach (var p in notes.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String) continue;
            var text = p.Value.GetString();
            first ??= text;
            if (p.Name.Equals(Loc.Instance.Language, StringComparison.OrdinalIgnoreCase)) return text;
            if (p.Name.Equals("en", StringComparison.OrdinalIgnoreCase)) english = text;
        }
        return english ?? first;
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"HTTP {(int)code}",
        HttpRequestException => Loc.T("updates.noConnection"),
        TaskCanceledException => Loc.T("updates.timeout"),
        JsonException or FormatException or KeyNotFoundException or ArgumentException => Loc.T("updates.badManifest"),
        _ => ex.Message,
    };

    // ── download ─────────────────────────────────────────────────────────

    public static string UpdatesDir => Path.Combine(AppPaths.DataDir, "Updates");

    /// <summary>Downloads and verifies the available version. Returns the file, or null when it failed.</summary>
    public async Task<string?> DownloadAsync()
    {
        var info = Available;
        if (info is null) return null;
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (State is UpdateState.Downloading or UpdateState.Ready) return ReadyFile;
            _download?.Dispose();
            _download = cts = new CancellationTokenSource();
        }
        Progress = 0;
        Set(UpdateState.Downloading);
        var file = Path.Combine(UpdatesDir, $"KDesktop-{info.Version.ToString(3)}.exe");
        var partial = file + ".partial";
        try
        {
            Directory.CreateDirectory(UpdatesDir);
            foreach (var stale in Directory.GetFiles(UpdatesDir)) TryDelete(stale);

            using (var response = await _http.GetAsync(info.Download, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.Size;
                await using var input = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                await using var output = File.Create(partial);
                var buffer = new byte[1 << 16];
                long done = 0;
                var lastReport = Stopwatch.StartNew();
                int read;
                while ((read = await input.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
                    done += read;
                    if (total > 0 && lastReport.ElapsedMilliseconds > 150)
                    {
                        Progress = Math.Min(1, done / (double)total);
                        lastReport.Restart();
                        Changed?.Invoke();
                    }
                }
            }

            Verify(partial, info);
            File.Move(partial, file, overwrite: true);
            ReadyFile = file;
            Progress = 1;
            Log.Info($"Actualización {info.Version} descargada y verificada: {file}");
            Set(UpdateState.Ready);
            return file;
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            if (cts.IsCancellationRequested)
            {
                Set(UpdateState.Available);
                return null;
            }
            Log.Warn($"No se pudo descargar la actualización {info.Version}: {ex.Message}");
            Set(UpdateState.Error, ex is InvalidDataException ? ex.Message : Describe(ex));
            return null;
        }
    }

    public void CancelDownload()
    {
        lock (_gate) _download?.Cancel();
    }

    /// <summary>Checks the hash (when the manifest has one) and that the file really is that version of K-Desktop.</summary>
    private static void Verify(string file, UpdateInfo info)
    {
        if (info.Sha256 is { } expected)
        {
            using var stream = File.OpenRead(file);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Loc.T("updates.badHash"));
        }
        var fileInfo = FileVersionInfo.GetVersionInfo(file);
        var product = fileInfo.ProductName ?? string.Empty;
        var version = new Version(fileInfo.FileMajorPart, fileInfo.FileMinorPart, fileInfo.FileBuildPart);
        if (!product.StartsWith("K-Desktop", StringComparison.OrdinalIgnoreCase) || version != info.Version)
            throw new InvalidDataException(Loc.T("updates.badFile"));
    }

    // ── install ──────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the downloaded exe in place of the running one and starts it; the caller then exits the app.
    /// <paramref name="arguments"/> are passed on to the new instance (its own --after-update is added here).
    /// </summary>
    public void Install(IEnumerable<string> arguments)
    {
        var source = ReadyFile ?? throw new InvalidOperationException("No hay ninguna actualización descargada");
        if (!CanInstall) throw new InvalidOperationException(Loc.T("updates.notPortable"));
        var exe = AppPaths.ExecutablePath;
        var old = exe + ".old";
        if (File.Exists(old) && !TryDelete(old)) old = $"{exe}.{DateTime.Now:yyyyMMddHHmmss}.old";

        File.Move(exe, old);
        try { File.Copy(source, exe); }
        catch
        {
            File.Move(old, exe); // put the running version back
            throw;
        }

        var args = arguments.Append(AfterUpdateArgument).Append(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(CurrentVersion.ToString(3));
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)!, Arguments = string.Join(' ', args.Select(Quote)) });
        Log.Info($"Actualización instalada: {CurrentVersion} → {Available?.Version}; reiniciando");
        TryDelete(source);
    }

    /// <summary>
    /// First thing the new version does: waits for the old process to leave (it holds the single-instance mutex) and
    /// removes what the update left behind. Returns the version it updated from, or null if this is a normal start.
    /// </summary>
    public static string? FinishPendingUpdate(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals(AfterUpdateArgument, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return null;
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
        {
            try
            {
                using var old = Process.GetProcessById(pid);
                old.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException) { /* already gone */ }
            catch (InvalidOperationException) { }
        }
        CleanUp();
        return i + 2 < args.Length ? args[i + 2] : "?";
    }

    /// <summary>Removes old exes (*.old next to this one) and leftover downloads.</summary>
    public static void CleanUp()
    {
        try
        {
            var exe = AppPaths.ExecutablePath;
            var dir = Path.GetDirectoryName(exe);
            if (dir is not null)
                foreach (var old in Directory.GetFiles(dir, Path.GetFileName(exe) + "*.old")) TryDelete(old);
            if (Directory.Exists(UpdatesDir))
                foreach (var file in Directory.GetFiles(UpdatesDir, "*.partial")) TryDelete(file);
        }
        catch (Exception ex) { Log.Warn("Limpieza tras actualizar: " + ex.Message); }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
            return true;
        }
        catch { return false; }
    }

    private static string Quote(string a) => a.Length > 0 && !a.Contains(' ') && !a.Contains('"') ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    public void Dispose()
    {
        CancelDownload();
        _http.Dispose();
    }
}
