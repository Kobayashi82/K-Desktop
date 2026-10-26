using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop;

/// <summary>
/// What the assistants know (stage 19), from K-Desktop's side: the folders are a setting (sent in <c>config</c> <c>tools</c>);
/// the assistant process reads them and keeps the index; Options → «Lo que sabe» asks it (<c>knowledge</c>) and shows what
/// comes back (<c>knowledgeState</c>, <c>knowledgeFiles</c>, <c>knowledgeFound</c>). Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>How the index is, as the process last said (null until it does, or with the process stopped).</summary>
    public KnowledgeStateMessage? AssistantKnowledge { get; private set; }

    /// <summary>Raised (UI thread) when <see cref="AssistantKnowledge"/> changes or the process goes away.</summary>
    public event Action? AssistantKnowledgeChanged;

    /// <summary>The files of a folder, as asked (UI thread).</summary>
    public event Action<KnowledgeFilesMessage>? AssistantKnowledgeFiles;

    /// <summary>What a search to try found (UI thread).</summary>
    public event Action<KnowledgeFoundMessage>? AssistantKnowledgeFound;

    public bool RequestAssistantKnowledge() => SendKnowledge(new KnowledgeMessage("state"));

    public bool RequestAssistantKnowledgeFiles(string folder, string? text = null, string? filter = null) =>
        SendKnowledge(new KnowledgeMessage("files", folder, text, filter));

    public bool SearchAssistantKnowledge(string text) => SendKnowledge(new KnowledgeMessage("search", Text: text));

    public bool RebuildAssistantKnowledge() => SendKnowledge(new KnowledgeMessage("rebuild"));

    /// <summary>
    /// Adds a folder of documents or a single file; null when it was added, else why not (<see cref="KnowledgeFolders.Problem"/>).
    /// A folder above some already chosen (folders or files) takes their place.
    /// </summary>
    public string? AddKnowledgeFolder(string folder)
    {
        var chosen = Config.Settings.AssistantKnowledgeFolders;
        if (KnowledgeFolders.Problem(folder, chosen, Config.Settings.AssistantKnowledgeOcr) is { } problem) return problem;
        var full = System.IO.Path.GetFullPath(folder).TrimEnd('\\');
        Config.Settings.AssistantKnowledgeFolders = [.. chosen.Where(c => !KnowledgeFolders.Contains(full, c)), full];
        Log.Info($"Asistente: añadido a lo que sabe: {full}");
        return null;
    }

    /// <summary>Takes a folder or a file out (what was read from it is forgotten).</summary>
    public void RemoveKnowledgeFolder(string folder)
    {
        var s = Config.Settings;
        s.AssistantKnowledgeFolders = [.. s.AssistantKnowledgeFolders.Where(c => !string.Equals(c, folder, StringComparison.OrdinalIgnoreCase))];
        // The files taken out of it no longer mean anything.
        if (s.AssistantKnowledgeSkipped.Any(f => KnowledgeFolders.Contains(folder, f)))
            s.AssistantKnowledgeSkipped = [.. s.AssistantKnowledgeSkipped.Where(f => !KnowledgeFolders.Contains(folder, f))];
        Log.Info($"Asistente: fuera de lo que sabe: {folder}");
    }

    /// <summary>Takes one file of a folder out (not read, what was read of it forgotten), or puts it back.</summary>
    public void SkipKnowledgeFile(string path, bool skip)
    {
        var s = Config.Settings;
        var skipped = s.AssistantKnowledgeSkipped;
        if (skip == skipped.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        s.AssistantKnowledgeSkipped = skip ? [.. skipped, path] : [.. skipped.Where(f => !string.Equals(f, path, StringComparison.OrdinalIgnoreCase))];
        Log.Info(skip ? $"Asistente: archivo quitado de lo que sabe: {path}" : $"Asistente: archivo de vuelta en lo que sabe: {path}");
    }

    /// <summary>Forgets everything: no folders, nothing read.</summary>
    public void ForgetAllKnowledge()
    {
        Config.Settings.AssistantKnowledgeFolders = [];
        Config.Settings.AssistantKnowledgeSkipped = [];
        SendKnowledge(new KnowledgeMessage("clear"));
        Log.Info("Asistente: lo que sabe, olvidado entero");
    }

    private bool SendKnowledge(KnowledgeMessage message) => _assistant is { IsRunning: true } supervisor && supervisor.Send(message);

    private void OnAssistantKnowledge(KnowledgeStateMessage state)
    {
        AssistantKnowledge = state;
        AssistantKnowledgeChanged?.Invoke();
    }

    /// <summary>The process went away: what it sent no longer holds.</summary>
    private void ForgetAssistantKnowledge()
    {
        if (AssistantKnowledge is null) return;
        AssistantKnowledge = null;
        AssistantKnowledgeChanged?.Invoke();
    }
}
