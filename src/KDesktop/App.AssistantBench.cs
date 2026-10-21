using System.IO;
using KDesktop.Core;
using KDesktop.Core.Assistant.Bench;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// Options → Brain → Test the model (stage 3.9), from K-Desktop's side: the assistant process runs the bench; this
/// starts and cancels it (<c>bench</c>), shows how it goes (<c>benchState</c>) and opens the user's own bench to edit
/// it. Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>How the bench goes, as the process last said (null until it does, or with the process stopped).</summary>
    public BenchStateMessage? AssistantBench { get; private set; }

    /// <summary>Raised (UI thread) when the bench changes.</summary>
    public event Action? AssistantBenchChanged;

    public bool RequestAssistantBench() => SendBench(new BenchMessage("state"));

    public bool StartAssistantBench(string model) => SendBench(new BenchMessage("start", model));

    public bool CancelAssistantBench() => SendBench(new BenchMessage("cancel"));

    private bool SendBench(BenchMessage message) => _assistant is { IsRunning: true } supervisor && supervisor.Send(message);

    private void OnAssistantBench(BenchStateMessage state)
    {
        AssistantBench = state;
        AssistantBenchChanged?.Invoke();
    }

    private void ForgetAssistantBench()
    {
        if (AssistantBench is null) return;
        AssistantBench = null;
        AssistantBenchChanged?.Invoke();
    }

    /// <summary>Opens the user's bench (made from the built-in one the first time) in their editor, as the user.</summary>
    public void OpenAssistantBench()
    {
        try
        {
            var file = BenchFile.EnsureUserFile(AssistantLanguage);
            ProcessLauncher.Launch(new LaunchSpec(file), asAdmin: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido crear el banco de pruebas: {ex.Message}");
            Ui.ShowOsd(ex.Message, Core.Engine.OsdKind.Warning);
        }
    }
}
