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
    /// <summary>
    /// What each assistant published for its panel, by id: since it was last loaded, or (5.0.289) what it published the last
    /// time, kept on disk, so its panel and its galleries are not empty while it is not on the desktop.
    /// </summary>
    private readonly Dictionary<string, JsonObject> _assistantPanels = new(StringComparer.Ordinal);

    /// <summary>Assistants being loaded again: what they publish first replaces what was kept (keys of an older version go).</summary>
    private readonly HashSet<string> _assistantPanelsAfresh = new(StringComparer.Ordinal);

    /// <summary>Panels that changed and are not on disk yet (saved a moment later: an assistant may publish often).</summary>
    private readonly HashSet<string> _assistantPanelsToSave = new(StringComparer.Ordinal);

    private System.Windows.Threading.DispatcherTimer? _assistantPanelSave;

    /// <summary>Raised when an assistant published data for its panel; the argument is its id.</summary>
    public event Action<string>? AssistantPanelChanged;

    /// <summary>What an assistant published for its panel (now or the last time), or null if it never published anything.</summary>
    public JsonObject? AssistantPanelData(string id)
    {
        if (_assistantPanels.TryGetValue(id, out var data)) return data;
        var file = AssistantPaths.PanelCache(id);
        if (!File.Exists(file)) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) is not JsonObject kept) return null;
            _assistantPanels[id] = kept;
            return kept;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente «{id}»: no se ha podido leer su panel guardado ({ex.Message})");
            return null;
        }
    }

    private void OnAssistantPanel(PanelMessage panel)
    {
        if (panel.Assistant is not { } id) return;
        if (_assistantPanelsAfresh.Remove(id) || AssistantPanelData(id) is null) _assistantPanels[id] = [];
        AssistantUi.Merge(_assistantPanels[id], panel.Data);
        SaveAssistantPanelSoon(id);
        AssistantPanelChanged?.Invoke(id);
    }

    /// <summary>
    /// An assistant is being loaded again: it publishes its panel afresh (what was kept shows until then). With
    /// <paramref name="delete"/> (its data was deleted) what was kept goes too.
    /// </summary>
    private void ForgetAssistantPanel(string id, bool delete = false)
    {
        if (!delete)
        {
            _assistantPanelsAfresh.Add(id);
            return;
        }
        _assistantPanelsAfresh.Remove(id);
        _assistantPanelsToSave.Remove(id);
        try { File.Delete(AssistantPaths.PanelCache(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Asistente «{id}»: no se ha podido borrar su panel guardado ({ex.Message})"); }
        if (_assistantPanels.Remove(id)) AssistantPanelChanged?.Invoke(id);
    }

    private void SaveAssistantPanelSoon(string id)
    {
        _assistantPanelsToSave.Add(id);
        if (_assistantPanelSave is null)
        {
            _assistantPanelSave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _assistantPanelSave.Tick += (_, _) => SaveAssistantPanels();
        }
        if (!_assistantPanelSave.IsEnabled) _assistantPanelSave.Start();
    }

    /// <summary>Writes the panels that changed (also when K-Desktop closes).</summary>
    internal void SaveAssistantPanels()
    {
        _assistantPanelSave?.Stop();
        foreach (var id in _assistantPanelsToSave)
        {
            if (!_assistantPanels.TryGetValue(id, out var data)) continue;
            try
            {
                var file = AssistantPaths.PanelCache(id);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file + ".tmp", data.ToJsonString());
                File.Move(file + ".tmp", file, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Asistente «{id}»: no se ha podido guardar su panel ({ex.Message})");
            }
        }
        _assistantPanelsToSave.Clear();
    }

    /// <summary>Whether this assistant is on the desktop right now (it can take settings and run extras).</summary>
    public bool IsAssistantLoaded(string id) => _assistant is { IsRunning: true } supervisor && supervisor.IsLoaded(id);

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
        if (IsAssistantLoaded(assistant.Id)) _assistant!.Send(new ConfigMessage("assistantSettings", values.DeepClone(), assistant.Id));
        SendStageSettings(assistant.Id, (JsonObject)values.DeepClone());
    }

    /// <summary>Runs one of an assistant's extras (a button of its settings or its panel). It has to be on the desktop.</summary>
    public void RunAssistantExtra(string id, string extra)
    {
        if (IsAssistantLoaded(id))
        {
            Log.Info($"Asistente: extra «{extra}» de «{id}»");
            _assistant!.Send(new ExtraMessage(id, extra));
            return;
        }
        Ui.ShowOsd(Loc.T("assistantSettings.notOnDesktop", AssistantName(id)), Core.Engine.OsdKind.Info);
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
            UnloadAssistant(_assistant!, id, UnloadMessage.Reloading);
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
            ForgetAssistantPanel(id, delete: true);
            if (reload && IsOnDesktop(id) && _assistant is { IsRunning: true } supervisor)
                LoadAssistant(supervisor, id, LoadMessage.Startup); // its data is gone: a fresh start
            AssistantChanged?.Invoke();
        }
    }
}
