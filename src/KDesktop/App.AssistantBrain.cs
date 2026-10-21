using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// The assistants' brain from K-Desktop's side (stage 3.1): Options → Assistant → Brain only shows and asks. Finding the
/// server, installing Ollama and downloading models happen in the assistant process (as the normal user), which says how
/// it goes with <c>brainState</c>.
/// </summary>
public partial class App
{
    /// <summary>The <c>brain</c> section last sent (in the welcome or after a change), to send only changes.</summary>
    private string? _assistantBrainSent;

    /// <summary>The last state the process sent of its brain (null while it is not running).</summary>
    public BrainStateMessage? AssistantBrain { get; private set; }

    /// <summary>Raised (UI thread) when <see cref="AssistantBrain"/> changes.</summary>
    public event Action? AssistantBrainChanged;

    /// <summary>The brain's settings as the process gets them (the key in plain text: the pipe is only the user's).</summary>
    private JsonObject AssistantBrainConfig()
    {
        var s = Config.Settings;
        return new BrainConfig(s.AssistantBrainProvider, s.AssistantBrainEndpoint, AssistantBrainApiKey, s.AssistantBrainModel, s.AssistantModels,
            s.AssistantBrainServer, s.AssistantBrainServerArgs, s.AssistantBrainKeepMinutes).ToJson();
    }

    /// <summary>An assistant's own model (stage 3.2); null or empty: the general one.</summary>
    public void SetAssistantModel(string assistant, string? model)
    {
        var models = new Dictionary<string, string>(Config.Settings.AssistantModels, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(model)) models.Remove(assistant);
        else models[assistant] = model.Trim();
        Config.Settings.AssistantModels = models;
        Log.Info($"Asistente: modelo de «{assistant}»: {(string.IsNullOrWhiteSpace(model) ? "el general" : model)}");
        AssistantChanged?.Invoke();
    }

    /// <summary>
    /// The model an assistant suggests when Ollama does not have it yet (to offer it, never to download it without asking);
    /// null when it suggests none, it is there, the brain is not Ollama or nothing is known of it yet.
    /// </summary>
    public string? MissingSuggestedModel(AssistantManifest manifest) =>
        manifest.Model?.Suggested is { Length: > 0 } suggested
        && Config.Settings.AssistantBrainProvider == BrainProvider.Ollama
        && AssistantBrain is { State: "ready" or "noModels" or "modelMissing", Models: { } models }
        && !models.Any(m => BrainProviders.SameModel(m.Name, suggested))
            ? suggested : null;

    /// <summary>The API key in plain text (empty when there is none or it was saved on another PC).</summary>
    public string AssistantBrainApiKey =>
        Config.Settings.AssistantBrainApiKey is { Length: > 0 } stored ? Secrets.Unprotect(stored) ?? string.Empty : string.Empty;

    public void SetAssistantBrainApiKey(string key) =>
        Config.Settings.AssistantBrainApiKey = key.Trim() is { Length: > 0 } plain ? Secrets.Protect(plain) : string.Empty;

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
    public void AskAssistantBrain(string op, string? model = null) => _assistant?.Send(new BrainMessage(op, model));

    /// <summary>
    /// «Asistente: cerebro» (and the tray): the model out of the graphics card, or loaded now. Without the module running,
    /// nothing of it is loaded: said in the OSD.
    /// </summary>
    private void BrainFromCommand(Core.Model.AssistantBrainOp op)
    {
        if (_assistant is not { IsRunning: true } || Config.Settings.AssistantBrainProvider == BrainProvider.None)
        {
            Ui.ShowOsd(Loc.T(op == Core.Model.AssistantBrainOp.Unload ? "osd.brainNothing" : "assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return;
        }
        var loaded = AssistantBrain is { Loaded.Count: > 0 } or { Loading: true };
        AskAssistantBrain(op == Core.Model.AssistantBrainOp.Unload ? "unload" : "load");
        Ui.ShowOsd(Loc.T(op == Core.Model.AssistantBrainOp.Load ? "osd.brainLoading" : loaded ? "osd.brainFreed" : "osd.brainNothing"));
        Log.Info($"Asistente: cerebro {(op == Core.Model.AssistantBrainOp.Unload ? "descargado" : "cargado")} con la acción");
    }

    private void OnAssistantBrainState(BrainStateMessage state)
    {
        AssistantBrain = state;
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
