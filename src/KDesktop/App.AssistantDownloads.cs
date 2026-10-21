using System.IO;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop;

/// <summary>
/// Models of Hugging Face for the brain (5.0.107), downloaded by K-Desktop as it does with Kokoro and Parakeet:
/// resumable, checked with their SHA-256, one at a time, into <see cref="AssistantModelsFolder"/> (a folder per
/// repository, so the vision projector is found next to its model). Also deleting a downloaded model and taking the
/// whole folder to another drive.
/// </summary>
public partial class App
{
    /// <summary>A download going on (or how the last one ended).</summary>
    public sealed record ModelDownload(string Name, long Done, long Total, bool Running, string? Error, string ModelPath, string? ForAssistant);

    private ModelDownloader? _modelDownloader;

    /// <summary>The download going on or the last one (null: none in this run).</summary>
    public ModelDownload? AssistantModelDownload { get; private set; }

    /// <summary>Raised (UI thread) on the download's progress, start and end.</summary>
    public event Action? AssistantModelDownloadChanged;

    /// <summary>
    /// Downloads a model (and its projector). <paramref name="forAssistant"/>: it was suggested by that assistant, which
    /// thinks with it once it is here. False when another download is going on.
    /// </summary>
    public bool StartModelDownload(HfModel model, HfModel? projector, string? forAssistant = null)
    {
        if (_modelDownloader is { IsDownloading: true }) return false;
        var (files, path) = HuggingFace.FilesOf(model, projector, AssistantModelsFolder);
        var downloader = new ModelDownloader(() => files, $"el modelo {model.Name}");
        _modelDownloader?.Dispose();
        _modelDownloader = downloader;
        var total = files.Sum(f => f.Size);
        AssistantModelDownload = new ModelDownload(model.Name, 0, total, true, null, path, forAssistant);
        downloader.Changed += () => Dispatcher.BeginInvoke(() => OnModelDownload(downloader));
        Log.Info($"Asistente: descargando {model.Repo}/{model.File}{(projector is null ? "" : " con " + projector.File)} ({total / 1e9:0.0} GB) en {AssistantModelsFolder}");
        downloader.Start();
        return true;
    }

    public void CancelModelDownload() => _modelDownloader?.Cancel();

    private void OnModelDownload(ModelDownloader downloader)
    {
        if (downloader != _modelDownloader || AssistantModelDownload is not { } now) return;
        var running = downloader.IsDownloading;
        AssistantModelDownload = now with { Done = downloader.Done, Running = running, Error = downloader.Error };
        if (now.Running && !running)
        {
            if (downloader.Error is null && File.Exists(now.ModelPath))
            {
                Ui.ShowOsd(Loc.T("brainModels.downloaded", now.Name));
                if (now.ForAssistant is { } assistant) SetAssistantModel(assistant, now.ModelPath);
                AskAssistantBrain("rescan");
            }
            else if (downloader.Error is { } error)
            {
                Ui.ShowOsd(Loc.T("brainModels.downloadFailed", now.Name, error), Core.Engine.OsdKind.Warning);
            }
        }
        AssistantModelDownloadChanged?.Invoke();
    }

    /// <summary>Whether a model is one K-Desktop downloaded (only those can be deleted from Options).</summary>
    public bool IsDownloadedModel(string path) => ModelRefs.IsLocal(path) && GgufFiles.IsInside(path, AssistantModelsFolder);

    /// <summary>
    /// Deletes a downloaded model (out of the graphics card first) and, when nothing else is left in its folder, its
    /// projector and the folder. Whether it could.
    /// </summary>
    public async Task<bool> DeleteDownloadedModelAsync(string path)
    {
        if (!IsDownloadedModel(path)) return false;
        if (AssistantBrain?.Loaded?.Any(m => ModelRefs.Same(m, path)) == true || AssistantBrain?.Loading is { } loading && ModelRefs.Same(loading, path))
        {
            AskAssistantBrain("unload");
            await Task.Delay(1500).ConfigureAwait(true);
        }
        var folder = Path.GetDirectoryName(path)!;
        var ok = await Task.Run(() =>
        {
            try
            {
                foreach (var part in Directory.GetFiles(folder, "*.gguf").Where(f => SamePart(f, path))) File.Delete(part);
                if (!Directory.GetFiles(folder, "*.gguf").Any(f => GgufFiles.IsModel(Path.GetFileName(f))))
                {
                    foreach (var rest in Directory.GetFiles(folder, "*.gguf")) File.Delete(rest);
                    if (!Directory.EnumerateFileSystemEntries(folder).Any() && !string.Equals(folder.TrimEnd('\\'), AssistantModelsFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(folder);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Asistente: no se ha podido borrar {path}: {ex.Message}");
                return false;
            }
        }).ConfigureAwait(true);
        if (ok)
        {
            Log.Info($"Asistente: modelo borrado: {path}");
            var s = Config.Settings;
            if (ModelRefs.Same(s.AssistantBrainModel, path)) s.AssistantBrainModel = string.Empty;
            if (s.AssistantModels.Values.Any(m => ModelRefs.Same(m, path)))
                s.AssistantModels = s.AssistantModels.Where(p => !ModelRefs.Same(p.Value, path)).ToDictionary(p => p.Key, p => p.Value);
        }
        AskAssistantBrain("rescan");
        return ok;
    }

    /// <summary>The file itself or another part of the same split model.</summary>
    private static bool SamePart(string file, string model)
    {
        if (string.Equals(file, model, StringComparison.OrdinalIgnoreCase)) return true;
        var split = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(model), @"^(.*-)00001-of-\d+\.gguf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return split.Success && Path.GetFileName(file).StartsWith(split.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the downloads folder is being moved now.</summary>
    public bool MovingModelsFolder { get; private set; }

    /// <summary>
    /// Takes the downloads folder elsewhere (they weigh a lot: another drive), moving what is in it (out of the graphics
    /// card first). The models chosen from there follow. Whether it went well.
    /// </summary>
    public async Task<bool> MoveModelsFolderAsync(string target)
    {
        var from = AssistantModelsFolder;
        target = Path.GetFullPath(target).TrimEnd('\\');
        if (MovingModelsFolder || string.Equals(from.TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase) || GgufFiles.IsInside(target, from)) return false;
        MovingModelsFolder = true;
        AssistantModelDownloadChanged?.Invoke();
        AskAssistantBrain("unload");
        await Task.Delay(1500).ConfigureAwait(true);
        Log.Info($"Asistente: la carpeta de modelos pasa de {from} a {target}");
        var error = await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(target);
                if (!Directory.Exists(from)) return null;
                foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
                {
                    var to = Path.Combine(target, Path.GetRelativePath(from, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(file, to, overwrite: true);
                }
                foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
                    if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
                return (string?)null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }).ConfigureAwait(true);
        MovingModelsFolder = false;
        if (error is not null)
        {
            Log.Warn($"Asistente: no se han podido mover los modelos: {error}");
            Ui.ShowOsd(Loc.T("brainModels.moveFailed", error), Core.Engine.OsdKind.Warning);
            AssistantModelDownloadChanged?.Invoke();
            return false;
        }
        var s = Config.Settings;
        string Moved(string model) => IsInsideFolder(model, from) ? Path.Combine(target, Path.GetRelativePath(from, model)) : model;
        s.AssistantBrainDownloads = string.Equals(target, Path.Combine(AppPaths.ModelsDir, "llm"), StringComparison.OrdinalIgnoreCase) ? string.Empty : target;
        s.AssistantBrainModel = Moved(s.AssistantBrainModel);
        s.AssistantModels = s.AssistantModels.ToDictionary(p => p.Key, p => Moved(p.Value));
        Ui.ShowOsd(Loc.T("brainModels.moved", target));
        AskAssistantBrain("rescan");
        AssistantModelDownloadChanged?.Invoke();
        return true;
    }

    private static bool IsInsideFolder(string model, string folder) => ModelRefs.IsLocal(model) && GgufFiles.IsInside(model, folder);
}
