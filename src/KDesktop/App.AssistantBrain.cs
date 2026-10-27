using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// The assistants' brain from K-Desktop's side (stage 3.1; its own since 5.0.107): Options → Assistant → Brain keeps the
/// llama-servers, the folders of models and the other servers, and asks; finding the models, starting llama-server and
/// talking to the other servers happen in the assistant process (as the normal user), which says how it goes with
/// <c>brainState</c>. Downloading from Hugging Face is K-Desktop's (<c>App.AssistantDownloads.cs</c>).
/// </summary>
public partial class App
{
    /// <summary>The <c>brain</c> section last sent (in the welcome or after a change), to send only changes.</summary>
    private string? _assistantBrainSent;

    /// <summary>The last state the process sent of its brain (null while it is not running).</summary>
    public BrainStateMessage? AssistantBrain { get; private set; }

    /// <summary>
    /// The models the brain had the last time it said (5.0.289): an assistant's options list them while the process is
    /// stopped too, instead of only «Global».
    /// </summary>
    public IReadOnlyList<BrainModel> KnownBrainModels => AssistantBrain?.Models ?? _knownBrainModels;

    private IReadOnlyList<BrainModel> _knownBrainModels = [];

    /// <summary>Raised (UI thread) when <see cref="AssistantBrain"/> changes.</summary>
    public event Action? AssistantBrainChanged;

    /// <summary>Where the models downloaded from Hugging Face go (and the first folder looked in).</summary>
    public string AssistantModelsFolder =>
        Config.Settings.AssistantBrainDownloads is { Length: > 0 } chosen ? chosen : Path.Combine(AppPaths.ModelsDir, "llm");

    /// <summary>The brain's settings as the process gets them (the keys in plain text: the pipe is only the user's).</summary>
    private JsonObject AssistantBrainConfig()
    {
        var s = Config.Settings;
        return new BrainConfig(s.AssistantBrainModel, s.AssistantModels, s.AssistantBrainServers,
            [AssistantModelsFolder, .. s.AssistantBrainFolders.Where(f => !string.Equals(f, AssistantModelsFolder, StringComparison.OrdinalIgnoreCase))],
            s.AssistantBrainModelArgs, s.AssistantBrainKeepMinutes, s.AssistantBrainOneAtATime, s.AssistantBrainVision,
            [.. s.AssistantBrainRemotes.Select(r => r with { ApiKey = Unprotect(r.ApiKey) })], s.AssistantBrainThinkModel, s.AssistantBrainThinkTokens, s.AssistantBrainFreeForGames,
            s.AssistantThinkModels, s.AssistantBrainThinkReason, s.AssistantThinkReasons, s.AssistantThinkTokens).ToJson();
    }

    private static string Unprotect(string stored) => stored.Length > 0 ? Secrets.Unprotect(stored) ?? string.Empty : string.Empty;

    /// <summary>Whether the user chose no AI.</summary>
    public bool AssistantBrainOff => Config.Settings.AssistantBrainModel == ModelRefs.None;

    /// <summary>An assistant's own model (stage 3.2); null or empty: the general one.</summary>
    public void SetAssistantModel(string assistant, string? model)
    {
        var models = new Dictionary<string, string>(Config.Settings.AssistantModels, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(model)) models.Remove(assistant);
        else models[assistant] = model.Trim();
        Config.Settings.AssistantModels = models;
        Log.Info($"Asistente: modelo de «{assistant}»: {(string.IsNullOrWhiteSpace(model) ? "el general" : BrainProviders.ModelLabel(model))}");
        AssistantChanged?.Invoke();
    }

    /// <summary>How much an assistant may reason when it thinks harder (5.0.290); null: as «Para pensar más».</summary>
    public void SetAssistantThinkTokens(string assistant, int? tokens)
    {
        var budgets = new Dictionary<string, int>(Config.Settings.AssistantThinkTokens, StringComparer.Ordinal);
        if (tokens is { } own) budgets[assistant] = Math.Max(BrainConfig.LeastThinkTokens, own);
        else budgets.Remove(assistant);
        Config.Settings.AssistantThinkTokens = budgets;
        Log.Info($"Asistente: «{assistant}» razona al pensar más {(tokens is { } t ? t + " tokens" : "lo general")}");
        AssistantChanged?.Invoke();
    }

    /// <summary>
    /// An assistant's own model to think harder with (stage 13.10); null or empty: the general one; <see cref="ModelRefs.Usual"/>:
    /// its usual one. <paramref name="reason"/> (5.0.289): whether that model reasons before answering (null: as it was).
    /// </summary>
    public void SetAssistantThinkModel(string assistant, string? model, bool? reason = null)
    {
        var models = new Dictionary<string, string>(Config.Settings.AssistantThinkModels, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(model)) models.Remove(assistant);
        else models[assistant] = model.Trim();
        var reasons = new Dictionary<string, bool>(Config.Settings.AssistantThinkReasons, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(model) || reason == true) reasons.Remove(assistant);
        else if (reason == false) reasons[assistant] = false;
        Config.Settings.AssistantThinkReasons = reasons;
        Config.Settings.AssistantThinkModels = models;
        var what = string.IsNullOrWhiteSpace(model) ? "el general" : model == ModelRefs.Usual ? "su modelo" : BrainProviders.ModelLabel(model);
        Log.Info($"Asistente: «{assistant}» piensa más con {what}{(reasons.ContainsKey(assistant) ? ", sin razonar" : "")}");
        AssistantChanged?.Invoke();
    }

    // ── llama-servers and folders ────────────────────────────────────────

    /// <summary>Adds a llama-server.exe, and the folder of models next to it if there is one (Bonsai's demo keeps them in «models»).</summary>
    public void AddBrainServer(string path)
    {
        var s = Config.Settings;
        path = Path.GetFullPath(path.Trim().Trim('"'));
        if (s.AssistantBrainServers.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        s.AssistantBrainServers = [.. s.AssistantBrainServers, path];
        Log.Info($"Asistente: llama-server añadido: {path}");
        if (GgufFiles.ModelsFolderNear(path) is { } folder) AddBrainFolder(folder);
    }

    public void RemoveBrainServer(string path) =>
        Config.Settings.AssistantBrainServers = [.. Config.Settings.AssistantBrainServers.Where(s => !string.Equals(s, path, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Makes a llama-server the usual one (the first: it opens the models that are not near another one).</summary>
    public void MakeBrainServerFirst(string path) =>
        Config.Settings.AssistantBrainServers = [path, .. Config.Settings.AssistantBrainServers.Where(s => !string.Equals(s, path, StringComparison.OrdinalIgnoreCase))];

    public void AddBrainFolder(string folder)
    {
        var s = Config.Settings;
        folder = Path.GetFullPath(folder.Trim().Trim('"')).TrimEnd('\\');
        if (string.Equals(folder, AssistantModelsFolder, StringComparison.OrdinalIgnoreCase) || s.AssistantBrainFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        s.AssistantBrainFolders = [.. s.AssistantBrainFolders, folder];
        Log.Info($"Asistente: carpeta de modelos añadida: {folder}");
    }

    public void RemoveBrainFolder(string folder) =>
        Config.Settings.AssistantBrainFolders = [.. Config.Settings.AssistantBrainFolders.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))];

    // ── other servers ────────────────────────────────────────────────────

    /// <summary>Adds another server (its key protected) and gives back its id; null when that address is already there.</summary>
    public string? AddBrainRemote(RemoteKind kind, string endpoint, string apiKey)
    {
        var s = Config.Settings;
        var address = new BrainRemote("", kind, NormalEndpoint(kind, endpoint)).Address;
        if (s.AssistantBrainRemotes.Any(r => BrainProviders.SameAddress(r.Address, address))) return null;
        var n = 1;
        while (s.AssistantBrainRemotes.Any(r => r.Id == $"r{n}")) n++;
        var remote = new BrainRemote($"r{n}", kind, NormalEndpoint(kind, endpoint), Protect(apiKey));
        s.AssistantBrainRemotes = [.. s.AssistantBrainRemotes, remote];
        Log.Info($"Asistente: servidor de modelos añadido: {remote.Name} ({remote.Id})");
        return remote.Id;
    }

    /// <summary>Changes another server (null: that part stays).</summary>
    public void UpdateBrainRemote(string id, RemoteKind? kind = null, string? endpoint = null, string? apiKey = null)
    {
        var s = Config.Settings;
        s.AssistantBrainRemotes = [.. s.AssistantBrainRemotes.Select(r => r.Id != id ? r : r with
        {
            Kind = kind ?? r.Kind,
            Endpoint = endpoint is null ? r.Endpoint : NormalEndpoint(kind ?? r.Kind, endpoint),
            ApiKey = apiKey is null ? r.ApiKey : Protect(apiKey),
        })];
    }

    /// <summary>Removes another server; the models chosen from it are forgotten (they would never be there).</summary>
    public void RemoveBrainRemote(string id)
    {
        var s = Config.Settings;
        s.AssistantBrainRemotes = [.. s.AssistantBrainRemotes.Where(r => r.Id != id)];
        bool From(string model) => ModelRefs.SplitRemote(model)?.Remote == id;
        if (From(s.AssistantBrainModel)) s.AssistantBrainModel = string.Empty;
        if (s.AssistantModels.Values.Any(From)) s.AssistantModels = s.AssistantModels.Where(p => !From(p.Value)).ToDictionary(p => p.Key, p => p.Value);
        if (From(s.AssistantBrainThinkModel)) s.AssistantBrainThinkModel = string.Empty;
        if (s.AssistantThinkModels.Values.Any(From)) s.AssistantThinkModels = s.AssistantThinkModels.Where(p => !From(p.Value)).ToDictionary(p => p.Key, p => p.Value);
        Log.Info($"Asistente: servidor de modelos {id} quitado");
    }

    /// <summary>A model's own llama-server options (empty: none).</summary>
    public string BrainModelArgs(string model) =>
        Config.Settings.AssistantBrainModelArgs.FirstOrDefault(p => ModelRefs.Same(p.Key, model)).Value ?? string.Empty;

    /// <summary>Keeps a model's own llama-server options (empty: it has none); they apply the next time it loads.</summary>
    public void SetBrainModelArgs(string model, string args)
    {
        args = args.Trim();
        if (args == BrainModelArgs(model)) return;
        var all = Config.Settings.AssistantBrainModelArgs.Where(p => !ModelRefs.Same(p.Key, model)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (args.Length > 0) all[model] = args;
        Config.Settings.AssistantBrainModelArgs = all;
        Log.Info($"Asistente: opciones de llama-server de {BrainProviders.ModelLabel(model)}: {(args.Length > 0 ? args : "ninguna")}");
    }

    /// <summary>The key of another server in plain text (empty when there is none or it was saved on another PC).</summary>
    public string BrainRemoteKey(string id) =>
        Config.Settings.AssistantBrainRemotes.FirstOrDefault(r => r.Id == id) is { ApiKey: var stored } ? Unprotect(stored) : string.Empty;

    private static string Protect(string key) => key.Trim() is { Length: > 0 } plain ? Secrets.Protect(plain) : string.Empty;

    /// <summary>The address as kept: without the final slash, and empty when it is the usual one.</summary>
    private static string NormalEndpoint(RemoteKind kind, string endpoint)
    {
        var typed = endpoint.Trim().TrimEnd('/');
        if (typed.Length > 0 && !typed.Contains("://", StringComparison.Ordinal)) typed = "http://" + typed;
        return string.Equals(typed, kind.DefaultEndpoint(), StringComparison.OrdinalIgnoreCase) ? string.Empty : typed;
    }

    /// <summary>The name of a model for the user, with where it is when that tells it apart («qwen3:4b · Ollama · localhost:11434»).</summary>
    public string BrainModelName(string model)
    {
        var label = BrainProviders.ModelLabel(model);
        if (ModelRefs.SplitRemote(model) is { } remote)
            return Config.Settings.AssistantBrainRemotes.FirstOrDefault(r => r.Id == remote.Remote) is { } server ? $"{label} · {server.Name}" : label;
        return label;
    }

    // ── what an assistant suggests (stage 3.2; Hugging Face since 5.0.107) ─

    /// <summary>A model of Hugging Face to download for an assistant, and its vision projector.</summary>
    public sealed record SuggestedModel(HfModel Model, HfModel? Projector)
    {
        public long Size => Model.Size + (Projector?.Size ?? 0);
    }

    private readonly Dictionary<string, Task<IReadOnlyList<HfModel>>> _hfLists = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>.gguf</c> of a repository of Hugging Face (asked once per run; a failure is asked again next time).</summary>
    public Task<IReadOnlyList<HfModel>> HuggingFaceListAsync(string repo)
    {
        lock (_hfLists)
        {
            if (_hfLists.TryGetValue(repo, out var known) && !known.IsFaulted && !known.IsCanceled) return known;
            var task = HuggingFace.ListAsync(HfHttp, repo, CancellationToken.None);
            _hfLists[repo] = task;
            return task;
        }
    }

    private static readonly Lazy<HttpClient> HfClient = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppPaths.VersionName.Replace(' ', '-'));
        return http;
    });

    public static HttpClient HfHttp => HfClient.Value;

    /// <summary>
    /// The model an assistant suggests that is not on this PC yet (to offer it, never to download it without asking): the
    /// first of its suggestions that fits in the graphics card (the last one if none does). Null when it suggests none, one
    /// of them is already here, nothing is known of the brain yet, or Hugging Face does not answer. With
    /// <paramref name="thinking"/>, the one it suggests to think harder with (stage 13.10; null as well when thinking harder
    /// is off, or when it is the same file as its usual suggestion).
    /// </summary>
    public async Task<SuggestedModel?> MissingSuggestedModelAsync(AssistantManifest manifest, bool thinking = false)
    {
        if (thinking && Config.Settings.AssistantBrainThinkModel == ModelRefs.None) return null;
        var wanted = (thinking ? manifest.Model?.ThinkModels : manifest.Model?.Models) ?? [];
        if (wanted.Count == 0 || AssistantBrainOff || AssistantBrain is not { Models: { } have } brain) return null;
        var files = have.Where(m => ModelRefs.IsLocal(m.Name)).Select(m => Path.GetFileName(m.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var room = brain.VideoMemoryGb is { } gb ? (gb - 2.5) * 1e9 : double.MaxValue;
        SuggestedModel? last = null;
        foreach (var reference in wanted)
        {
            IReadOnlyList<HfModel> models;
            try { models = await HuggingFaceListAsync(reference.Repo).ConfigureAwait(true); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                Log.Warn($"Asistente: no se ha podido mirar {reference.Repo} en Hugging Face: {ex.Message}");
                continue;
            }
            if (HuggingFace.Pick(models, reference) is not { } model) continue;
            if (files.Contains(Path.GetFileName(model.File))) return null;
            // The same file as its usual suggestion: thinking harder with it is what happens anyway.
            if (thinking && await MissingSuggestedModelAsync(manifest).ConfigureAwait(true) is { } usual
                && string.Equals(Path.GetFileName(usual.Model.File), Path.GetFileName(model.File), StringComparison.OrdinalIgnoreCase)) return null;
            var suggestion = new SuggestedModel(model, Config.Settings.AssistantBrainVision ? HuggingFace.ProjectorFor(models, model) : null);
            if (suggestion.Size <= room) return suggestion;
            last = suggestion;
        }
        return last;
    }

    // ── asking ───────────────────────────────────────────────────────────

    /// <summary>After a settings change: the brain section, if it changed (UI thread).</summary>
    private void SendAssistantBrainConfig()
    {
        var brain = AssistantBrainConfig();
        var json = brain.ToJsonString();
        if (json == _assistantBrainSent) return;
        _assistantBrainSent = json;
        _assistant?.Send(new ConfigMessage("brain", brain));
    }

    /// <summary>Asks the process something for Options → Assistant → Brain (see <see cref="BrainMessage"/>).</summary>
    public void AskAssistantBrain(string op, string? model = null, string? remote = null) => _assistant?.Send(new BrainMessage(op, model, remote));

    /// <summary>
    /// «Asistente: cerebro» (and the tray): the models out of the graphics card, or loaded now. Without the module running,
    /// nothing of it is loaded: said in the OSD.
    /// </summary>
    private void BrainFromCommand(Core.Model.AssistantBrainOp op)
    {
        if (_assistant is not { IsRunning: true } || AssistantBrainOff)
        {
            Ui.ShowOsd(Loc.T(op == Core.Model.AssistantBrainOp.Unload ? "osd.brainNothing" : "assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return;
        }
        var loaded = AssistantBrain is { Loaded.Count: > 0 } or { Loading: not null };
        AskAssistantBrain(op == Core.Model.AssistantBrainOp.Unload ? "unload" : "load");
        Ui.ShowOsd(Loc.T(op == Core.Model.AssistantBrainOp.Load ? "osd.brainLoading" : loaded ? "osd.brainFreed" : "osd.brainNothing"));
        Log.Info($"Asistente: cerebro {(op == Core.Model.AssistantBrainOp.Unload ? "descargado" : "cargado")} con la acción");
    }

    private void OnAssistantBrainState(BrainStateMessage state)
    {
        AssistantBrain = state;
        if (state.Models is { Count: > 0 } models) _knownBrainModels = models;
        AssistantBrainChanged?.Invoke();
    }

    /// <summary>The process stopped: nothing is known of its brain.</summary>
    private void ForgetAssistantBrain()
    {
        if (AssistantBrain is null) return;
        AssistantBrain = null;
        AssistantBrainChanged?.Invoke();
    }
}
