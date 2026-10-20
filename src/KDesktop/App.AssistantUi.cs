using System.IO;
using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop;

/// <summary>
/// What each assistant brings for K-Desktop's interface (§11.8, stage 1.9): its settings (settings.json, drawn in
/// Options → Assistants), its panel (panel.json, in Panel → Assistant, fed with what it publishes) and the buttons of
/// both, which run its extras. Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>What each assistant published for its panel, by id (since it was last loaded).</summary>
    private readonly Dictionary<string, JsonObject> _assistantPanels = new(StringComparer.Ordinal);

    /// <summary>Raised when an assistant published data for its panel; the argument is its id.</summary>
    public event Action<string>? AssistantPanelChanged;

    /// <summary>What an assistant published for its panel, or null if it has not published anything.</summary>
    public JsonObject? AssistantPanelData(string id) => _assistantPanels.GetValueOrDefault(id);

    private void OnAssistantPanel(PanelMessage panel)
    {
        if ((panel.Assistant ?? _assistant?.LoadedAssistant) is not { } id) return;
        if (!_assistantPanels.TryGetValue(id, out var data)) _assistantPanels[id] = data = [];
        AssistantUi.Merge(data, panel.Data);
        AssistantPanelChanged?.Invoke(id);
    }

    /// <summary>An assistant is being loaded again: it publishes its panel afresh (keys of an older version go).</summary>
    private void ForgetAssistantPanel(string id)
    {
        if (_assistantPanels.Remove(id)) AssistantPanelChanged?.Invoke(id);
    }

    /// <summary>Whether this assistant is on the desktop right now (it can take settings and run extras).</summary>
    public bool IsAssistantLoaded(string id) => _assistant is { IsRunning: true } supervisor && supervisor.LoadedAssistant == id;

    /// <summary>
    /// Saves what the user changed in an assistant's settings (<paramref name="saved"/>, only what differs from the
    /// defaults) and, if it is on the desktop, sends it all its values at once so the change shows straight away.
    /// </summary>
    public void SaveAssistantSettings(InstalledAssistant assistant, JsonObject saved, JsonObject values)
    {
        try { AssistantSettingsValues.WriteSaved(assistant.Id, saved); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente «{assistant.Id}»: no se han podido guardar sus ajustes ({ex.Message})");
            Ui.ShowOsd(Loc.T("assistantSettings.saveFailed", assistant.Manifest.Name, ex.Message), Core.Engine.OsdKind.Warning);
        }
        if (IsAssistantLoaded(assistant.Id)) _assistant!.Send(new ConfigMessage("assistantSettings", values.DeepClone()));
    }

    /// <summary>Runs one of an assistant's extras (a button of its settings or its panel). It has to be on the desktop.</summary>
    public void RunAssistantExtra(string id, string extra)
    {
        if (IsAssistantLoaded(id))
        {
            Log.Info($"Asistente: extra «{extra}» de «{id}»");
            _assistant!.Send(new ExtraMessage(extra));
            return;
        }
        var name = AssistantPackages.Find(id)?.Manifest.Name ?? id;
        Ui.ShowOsd(Loc.T("assistantSettings.notOnDesktop", name), Core.Engine.OsdKind.Info);
    }

    /// <summary>
    /// «Start from scratch» (a <c>resetData</c> button): deletes the assistant's own data (progress, statistics…).
    /// If it is on the desktop, it leaves first and comes back without it. Throws IOException.
    /// </summary>
    public async Task ResetAssistantDataAsync(string id)
    {
        var reload = IsAssistantLoaded(id);
        if (reload)
        {
            _assistant!.Send(new UnloadMessage("reloading"));
            _assistant.LoadedAssistant = null;
            await Task.Delay(ExitTime);
        }
        try
        {
            await Task.Run(() =>
            {
                var folder = AssistantPaths.Data(id);
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            });
            Log.Info($"Asistente: datos de «{id}» borrados");
        }
        finally
        {
            ForgetAssistantPanel(id);
            if (reload && Config.Settings.AssistantActive == id && _assistant is { IsRunning: true } supervisor)
                LoadActiveAssistant(supervisor, "startup");
            AssistantChanged?.Invoke();
        }
    }
}
