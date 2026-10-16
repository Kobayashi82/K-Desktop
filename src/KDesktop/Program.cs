using System.ComponentModel;
using System.Diagnostics;
using KDesktop.Core;
using KDesktop.Core.Platform;
using KDesktop.Services;

namespace KDesktop;

/// <summary>
/// Command line:
/// <list type="bullet">
/// <item><c>--menu "Name"</c> open a QuickMenu · <c>--run "Name"</c> run a command · <c>--show</c> · <c>--exit</c>
/// (forwarded to the running instance when there is one)</item>
/// <item><c>--startup</c> started by Windows (no window) · <c>--no-elevate</c> · <c>--no-hooks</c> · <c>--no-udp</c> · <c>--no-web</c></item>
/// <item><c>--data "folder"</c> use another data folder (an independent instance) · <c>--page name</c> open a page</item>
/// <item><c>--voice-test a.wav folder "phrase"…</c> voice diagnostics without microphone or UI (report in Logs)</item>
/// <item><c>--wake-test "name" folder</c> wake word set-up and verification from word*/speech*/test*.wav (report in Logs)</item>
/// </list>
/// </summary>
public static class Program
{
    public const string StartupArgument = "--startup";
    private static string MutexName => @"Local\KDesktop5.Instance" + AppPaths.InstanceSuffix;

    private static readonly string[] ForwardedArguments = ["--menu", "--run", "--show", "--exit", "--page"];

    [STAThread]
    public static int Main(string[] args)
    {
        // Helper mode: start a program as the normal (non-elevated) user and exit. No UI involved.
        if (args.Length >= 2 && args[0] == ProcessLauncher.HelperArgument)
            return ProcessLauncher.RunHelper(args[1]);

        // Alternative data folder (tests, portable copies). Must be set before anything reads AppPaths.
        if (ValueOf(args, "--data") is { } dataDir) AppPaths.UseDataDir(dataDir);

        // Voice diagnostics: transcribe / match and exit, without touching the running instance.
        var voiceTest = Array.FindIndex(args, a => a.Equals("--voice-test", StringComparison.OrdinalIgnoreCase));
        if (voiceTest >= 0) return VoiceTest(args.Skip(voiceTest + 1).Where(a => a != "--no-open").ToArray(), open: !args.Contains("--no-open"));
        var wakeTest = Array.FindIndex(args, a => a.Equals("--wake-test", StringComparison.OrdinalIgnoreCase));
        if (wakeTest >= 0 && args.Length > wakeTest + 2) return WakeTest(args[wakeTest + 1], args[wakeTest + 2], open: !args.Contains("--no-open"));

        // Started by the previous version after installing an update: wait until it has left, then tidy up.
        var updatedFrom = KDesktop.Core.Updates.UpdateService.FinishPendingUpdate(args);

        // Already running? Forward the request (works across elevation levels) and leave.
        var forward = args.Any(a => ForwardedArguments.Contains(a, StringComparer.OrdinalIgnoreCase));
        if (SingleInstance.SignalExisting(forward ? args : null)) return 0;

        // The main instance needs elevation so its hooks also see elevated windows.
        if (!ProcessLauncher.IsElevated && !args.Contains("--no-elevate"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, string.Join(' ', args.Select(Quote)))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
                return 0;
            }
            catch (Win32Exception)
            {
                // UAC declined: keep going without elevation (hooks won't work over elevated windows).
            }
        }

        using var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            SingleInstance.SignalExisting(forward ? args : null);
            return 0;
        }

        var app = new App
        {
            StartedByWindows = args.Contains(StartupArgument),
            UpdatedFrom = updatedFrom,
            Arguments = args,
        };
        app.InitializeComponent();
        return app.Run();
    }

    private static int VoiceTest(string[] inputs, bool open)
    {
        try
        {
            AppPaths.EnsureCreated();
            var store = new Core.Storage.ConfigStore();
            store.Load();
            var report = Core.Voice.VoiceDiagnostics.Run(inputs, store.Config);
            Log.Flush();
            if (open) Process.Start(new ProcessStartInfo(report) { UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("--voice-test", ex);
            Log.Flush();
            return 1;
        }
    }

    private static int WakeTest(string name, string folder, bool open)
    {
        try
        {
            AppPaths.EnsureCreated();
            var report = Core.Voice.VoiceDiagnostics.RunWake(name, folder);
            Log.Flush();
            if (open) Process.Start(new ProcessStartInfo(report) { UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("--wake-test", ex);
            Log.Flush();
            return 1;
        }
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    /// <summary>Value that follows <paramref name="name"/> ("--menu Principal").</summary>
    public static string? ValueOf(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
