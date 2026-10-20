using System.Diagnostics;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Host;

/// <summary>
/// <c>KDesktop.exe --assistant</c>: the assistant process. It starts only what it needs (no hooks, web, UDP, events
/// or command engine), never runs elevated, writes to Logs\assistant.log and lives until K-Desktop closes it (or
/// <c>--assistant --exit</c>). One per data folder.
/// </summary>
public static class AssistantProcess
{
    private static string Suffix => AppPaths.InstanceSuffix;
    private static string MutexName => @"Local\KDesktop5.Assistant" + Suffix;
    private static string ExitEventName => @"Local\KDesktop5.Assistant.Exit" + Suffix;

    /// <summary>Entry point from Program.Main (after <c>--data</c> was applied). Returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        Log.UseFile("assistant");
        var options = AssistantOptions.Parse(args);
        try
        {
            if (options.Exit) return SignalExit() ? 0 : 1;

            // The assistant, its AI, tools and packages never run as administrator (§2.1 of the plan).
            if (ProcessLauncher.IsElevated && !options.AllowElevated) return RelaunchAsUser(args);

            using var mutex = new Mutex(true, MutexName, out var created);
            if (!created)
            {
                Log.Warn("Ya hay un proceso del asistente para esta carpeta de datos: se cierra este");
                return 2;
            }
            using var exit = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);

            Log.Info($"── Proceso del asistente {AppPaths.FullVersion} iniciando (pid {Environment.ProcessId}, " +
                     $"elevado: {ProcessLauncher.IsElevated}, tubería: {options.Pipe ?? "ninguna"}" +
                     (options.DevFolder is { } dev ? $", desarrollo: {dev}" : "") + ")");
            // Until K-Desktop says otherwise (welcome), Windows' language.
            Loc.Instance.Load(null);

            using var session = new AssistantSession(options, Dispatcher.CurrentDispatcher);
            session.Start();
            // «--assistant --exit», or K-Desktop closing it when the pipe cannot.
            ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => session.Shutdown("señal de cierre"), null, Timeout.Infinite, true);
            Dispatcher.Run();
            Log.Info("Proceso del asistente cerrado");
            return session.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("El proceso del asistente se ha detenido por un error", ex);
            return 3;
        }
        finally
        {
            Log.Flush();
        }
    }

    private static bool SignalExit()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var exit)) return false;
        using (exit) exit.Set();
        return true;
    }

    private static int RelaunchAsUser(IReadOnlyList<string> args)
    {
        var arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        using var process = ProcessLauncher.StartAsUser(AppPaths.ExecutablePath, arguments);
        if (process is null)
        {
            Log.Error("El proceso del asistente no puede correr como administrador y no se pudo relanzar sin elevación");
            return 4;
        }
        Log.Info($"Asistente iniciado como administrador: relanzado sin elevación (pid {process.Id})");
        return 0;
    }
}
