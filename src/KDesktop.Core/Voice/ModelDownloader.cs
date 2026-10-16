using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace KDesktop.Core.Voice;

/// <summary>One file of a voice model: where it comes from, where it goes and how to check it.</summary>
public sealed record ModelFile(string Url, string Path, long Size, string Sha256);

/// <summary>
/// Downloads the voice models into <see cref="AppPaths.ModelsDir"/>: resumable (a <c>.part</c> file continues where
/// it stopped), checked by size and SHA-256, in the background (closing the window does not stop it).
/// </summary>
public sealed class ModelDownloader
{
    private const string Parakeet = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/";

    private const string OpenWakeWord = "https://github.com/dscripka/openWakeWord/releases/download/v0.5.1/";

    public static IReadOnlyList<ModelFile> Files { get; } =
    [
        new(OpenWakeWord + "melspectrogram.onnx", VoiceModels.WakeMelspec, 1_087_958, "ba2b0e0f8b7b875369a2c89cb13360ff53bac436f2895cced9f479fa65eb176f"),
        new(OpenWakeWord + "embedding_model.onnx", VoiceModels.WakeEmbedding, 1_326_578, "70d164290c1d095d1d4ee149bc5e00543250a7316b59f31d056cff7bd3075c1f"),
        new("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx",
            VoiceModels.VadFile, 643_854, "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6"),
        new(Parakeet + "tokens.txt", VoiceModels.ParakeetTokens, 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
        new(Parakeet + "decoder.int8.onnx", VoiceModels.ParakeetDecoder, 11_845_275, "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e"),
        new(Parakeet + "joiner.int8.onnx", VoiceModels.ParakeetJoiner, 6_355_277, "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3"),
        new(Parakeet + "encoder.int8.onnx", VoiceModels.ParakeetEncoder, 652_184_281, "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247"),
    ];

    public static long TotalSize => Files.Sum(f => f.Size);

    private CancellationTokenSource? _cts;

    public bool IsDownloading => _cts is not null;

    /// <summary>Bytes on disk of the current download, of <see cref="TotalSize"/>.</summary>
    public long Done { get; private set; }

    /// <summary>Error of the last attempt (null if it went well or was cancelled).</summary>
    public string? Error { get; private set; }

    /// <summary>Progress, start, end (any thread; progress at most ~10 times a second).</summary>
    public event Action? Changed;

    /// <summary>Size on disk of the installed model files.</summary>
    public static long InstalledSize() => Files.Where(f => File.Exists(f.Path)).Sum(f => new FileInfo(f.Path).Length);

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        Error = null;
        Done = 0;
        Changed?.Invoke();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            try
            {
                await DownloadAllAsync(token).ConfigureAwait(false);
                Log.Info("Modelos de voz descargados");
            }
            catch (OperationCanceledException)
            {
                Log.Info("Descarga de los modelos de voz cancelada");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Log.Error("Error descargando los modelos de voz", ex);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                Changed?.Invoke();
            }
        });
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Removes every model file (and partial downloads). Unload the recognizer first.</summary>
    public static void Delete()
    {
        foreach (var file in Files)
        {
            TryDelete(file.Path);
            TryDelete(file.Path + ".part");
        }
        try
        {
            if (Directory.Exists(VoiceModels.ParakeetDir) && !Directory.EnumerateFileSystemEntries(VoiceModels.ParakeetDir).Any())
                Directory.Delete(VoiceModels.ParakeetDir);
        }
        catch { /* not empty or in use */ }
    }

    private async Task DownloadAllAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppPaths.VersionName.Replace(' ', '-'));
        var finished = 0L;
        foreach (var file in Files)
        {
            if (File.Exists(file.Path) && new FileInfo(file.Path).Length == file.Size)
            {
                finished += file.Size;
                Report(finished, force: true);
                continue;
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path)!);
            await DownloadAsync(http, file, finished, token).ConfigureAwait(false);
            finished += file.Size;
            Report(finished, force: true);
        }
    }

    private async Task DownloadAsync(HttpClient http, ModelFile file, long before, CancellationToken token)
    {
        var part = file.Path + ".part";
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > file.Size) { File.Delete(part); have = 0; }

        if (have < file.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
            if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            // The server may ignore the range: then start over.
            if (have > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                have += read;
                Report(before + have);
            }
        }

        if (new FileInfo(part).Length != file.Size)
            throw new IOException($"{System.IO.Path.GetFileName(file.Path)}: descarga incompleta");
        string hash;
        await using (var check = File.OpenRead(part))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, token).ConfigureAwait(false));
        if (hash != file.Sha256)
        {
            File.Delete(part);
            throw new IOException($"{System.IO.Path.GetFileName(file.Path)}: el archivo descargado no es el esperado (hash)");
        }
        File.Move(part, file.Path, overwrite: true);
    }

    private long _lastReport;

    private void Report(long done, bool force = false)
    {
        Done = done;
        var now = Environment.TickCount64;
        if (!force && now - _lastReport < 100) return;
        _lastReport = now;
        Changed?.Invoke();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Warn($"No se pudo borrar {path}: {ex.Message}"); }
    }
}
