using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Views;
using KDesktop.Views.Assistant;

namespace KDesktop;

/// <summary>One line of Panel → Assistant → Activity.</summary>
/// <param name="Assistant">Who did it (null: K-Desktop).</param>
public sealed class ActivityItem(ActivityEntry entry, string? assistant)
{
    public ActivityEntry Entry { get; } = entry;

    public string? Assistant { get; } = assistant;

    /// <summary>Its undo was asked for (it is not offered again).</summary>
    public bool Undone { get; set; }
}

/// <summary>
/// The assistants' tools from K-Desktop's side (stage 3.3): the questions they ask before doing something (a small window,
/// 10 s), the tools the user trusts (<c>assistantTrustedTools</c>, sent as the <c>tools</c> section), and the activity they
/// report, kept in <c>Assistant\activity.json</c> with its «undo».
/// </summary>
public partial class App
{
    /// <summary>Lines of the activity kept (the oldest are forgotten).</summary>
    private const int KeptActivity = 200;

    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private string? _assistantToolsSent;
    private List<ActivityItem>? _activity;
    private readonly Dictionary<string, AskWindow> _questions = new(StringComparer.Ordinal);

    /// <summary>Raised (UI thread) when the activity changes.</summary>
    public event Action? AssistantActivityChanged;

    /// <summary>One more line of the activity (the phone shows it under the answer).</summary>
    internal event Action<ActivityItem>? AssistantDid;

    /// <summary>The activity, newest first.</summary>
    public IReadOnlyList<ActivityItem> AssistantActivity => [.. Activity().AsEnumerable().Reverse()];

    private static string ActivityFile => Path.Combine(AppPaths.DataDir, "Assistant", "activity.json");

    private JsonObject AssistantToolsConfig() => new()
    {
        ["trusted"] = new JsonArray([.. Config.Settings.AssistantTrustedTools.Select(t => (JsonNode)t)]),
        ["learn"] = Config.Settings.AssistantMemoryLearn,
        ["askBefore"] = Config.Settings.AssistantAskBefore,
        ["place"] = Config.Settings.AssistantHomePlace,
        ["knowledge"] = new JsonObject
        {
            ["folders"] = new JsonArray([.. Config.Settings.AssistantKnowledgeFolders.Select(f => (JsonNode)f)]),
            ["ocr"] = Config.Settings.AssistantKnowledgeOcr,
            ["paused"] = Config.Settings.AssistantKnowledgePaused,
            ["speed"] = Config.Settings.AssistantKnowledgeSpeed,
            ["skipped"] = new JsonArray([.. Config.Settings.AssistantKnowledgeSkipped.Select(f => (JsonNode)f)]),
        },
        ["settings"] = new JsonObject(Config.Settings.AssistantToolSettings.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            KeyValuePair.Create(p.Key, (JsonNode?)new JsonObject { ["enabled"] = p.Value.Enabled, ["risk"] = p.Value.Risk }))),
        // The process only gets the hash of the key.
        ["share"] = new JsonObject
        {
            ["enabled"] = Config.Settings.AssistantShareTools,
            ["scope"] = Config.Settings.AssistantShareScope,
            ["open"] = !Config.Settings.AssistantShareNeedsKey,
            ["key"] = AssistantShareKey is { } key ? Core.Assistant.Sharing.McpShare.HashOf(key) : string.Empty,
        },
    };

    /// <summary>The key programs give to use the shared tools (null until sharing is turned on once).</summary>
    public string? AssistantShareKey => Config.Settings.AssistantShareKey is { } stored ? Core.Platform.Secrets.Unprotect(stored) : null;

    /// <summary>A new key (also the first one): the programs that had the old one stop getting in.</summary>
    public string NewAssistantShareKey()
    {
        var key = Core.Assistant.Sharing.McpShare.NewKey();
        Config.Settings.AssistantShareKey = Core.Platform.Secrets.Protect(key);
        Log.Info("Asistente: clave nueva para las herramientas compartidas");
        return key;
    }

    /// <summary>What to paste into the program's MCP settings.</summary>
    public string AssistantShareConfig() =>
        Core.Assistant.Sharing.McpShare.ClientConfig(AppPaths.ExecutablePath, Config.Settings.AssistantShareNeedsKey ? AssistantShareKey ?? NewAssistantShareKey() : null,
            AppPaths.InstanceSuffix.Length == 0 ? null : AppPaths.DataDir);

    /// <summary>After a settings change: the tools section, if it changed (UI thread).</summary>
    private void SendAssistantToolsConfig()
    {
        var tools = AssistantToolsConfig();
        var json = tools.ToJsonString();
        if (json == _assistantToolsSent) return;
        _assistantToolsSent = json;
        _assistant?.Send(new ConfigMessage("tools", tools));
    }

    /// <summary>A tool asks before doing something: the window, and its answer back (UI thread).</summary>
    private async void OnAssistantAsk(AskMessage ask)
    {
        // In an order from Telegram: the owner answers, on the PC if they are there or by Telegram (telegram, phase 4.2).
        if (ask.Origin is { } origin)
        {
            _assistant?.Send(new AnswerMessage(ask.Id, await AskForTelegramAsync(origin, ask.Id, ask.Assistant, ask.Text)));
            return;
        }
        var choice = await AskUserAsync(ask.Id, ask.Assistant, ask.Text, ask.CanTrust, ask.Seconds);
        if (choice == "always" && !Config.Settings.AssistantTrustedTools.Contains(ask.Tool))
            Config.Settings.AssistantTrustedTools = [.. Config.Settings.AssistantTrustedTools, ask.Tool];
        _assistant?.Send(new AnswerMessage(ask.Id, choice));
    }

    /// <summary>
    /// Asks the user whether an assistant may do something (UI thread): the window at the bottom right, without taking
    /// the focus. <c>yes</c>, <c>always</c>, <c>no</c> or <c>timeout</c>.
    /// </summary>
    private async Task<string> AskUserAsync(string key, string? assistant, string text, bool canTrust, int seconds)
    {
        var who = FromProcess(assistant);
        var window = new AskWindow(AssistantName(who), AssistantLook.IconOf(who), text, canTrust, TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 30)));
        _questions[key] = window;
        window.Show();
        if (TalkedByVoice(assistant)) ListenForAnswer(window, AssistantName(who), canTrust, TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 30)));
        var choice = await window.Answer;
        _questions.Remove(key);
        Log.Info($"Asistente: «{text}» → {choice}");
        return choice;
    }

    /// <summary>The process went away: nobody waits for those answers any more.</summary>
    private void AbandonAssistantQuestions()
    {
        foreach (var window in _questions.Values.ToList()) window.Abandon();
        _questions.Clear();
    }

    /// <summary>A line of the activity (UI thread). An undo marks the line it undid.</summary>
    private void OnAssistantLog(LogMessage log)
    {
        var activity = Activity();
        if (log.Entry.Undid is { } undid && activity.FirstOrDefault(a => a.Entry.Undo == undid) is { } original) original.Undone = true;
        var item = new ActivityItem(log.Entry, log.Assistant);
        activity.Add(item);
        if (activity.Count > KeptActivity) activity.RemoveRange(0, activity.Count - KeptActivity);
        SaveActivity();
        AssistantActivityChanged?.Invoke();
        AssistantDid?.Invoke(item);
    }

    /// <summary>Panel → Assistant → Activity → «Undo».</summary>
    public void UndoAssistantActivity(ActivityItem item)
    {
        if (item.Entry.Undo is not { } undo || item.Undone || _assistant is not { IsRunning: true } supervisor) return;
        item.Undone = true;
        supervisor.Send(new UndoMessage(undo, item.Assistant));
        AssistantActivityChanged?.Invoke();
    }

    /// <summary>Forgets the activity: everyone's, or (<paramref name="assistant"/>, 5.0.289) only what that one did.</summary>
    public void ClearAssistantActivity(string? assistant = null)
    {
        if (assistant is null) Activity().Clear();
        else Activity().RemoveAll(item => IsActivityOf(item, assistant));
        SaveActivity();
        AssistantActivityChanged?.Invoke();
    }

    private List<ActivityItem> Activity()
    {
        if (_activity is not null) return _activity;
        try
        {
            var saved = File.Exists(ActivityFile) ? JsonSerializer.Deserialize<List<SavedActivity>>(File.ReadAllText(ActivityFile), ActivityJson) : null;
            _activity = [.. (saved ?? []).Select(s => new ActivityItem(s.Entry, s.Assistant) { Undone = s.Undone })];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido leer la actividad: {ex.Message}");
            _activity = [];
        }
        return _activity;
    }

    private void SaveActivity()
    {
        // Small and rare (one line per thing an assistant does): written at once, in order.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ActivityFile)!);
            File.WriteAllText(ActivityFile, JsonSerializer.Serialize(Activity().Select(a => new SavedActivity(a.Entry, a.Assistant, a.Undone)), ActivityJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar la actividad: {ex.Message}");
        }
    }

    private sealed record SavedActivity(ActivityEntry Entry, string? Assistant, bool Undone);

    /// <summary>The name of who did a line: the assistant, or K-Desktop without a body.</summary>
    public static string ActivityWho(ActivityItem item) => AssistantName(item.Assistant);

    /// <summary>Whether <paramref name="assistant"/> did it (K-Desktop's lines have no id).</summary>
    public static bool IsActivityOf(ActivityItem item, string assistant) =>
        Core.Assistant.AssistantTargeting.IsSystem(assistant)
            ? item.Assistant is null or "" || Core.Assistant.AssistantTargeting.IsSystem(item.Assistant)
            : item.Assistant == assistant;
}
