using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Updates;

namespace KDesktop;

public partial class App
{
    /// <summary>Checks at startup (after a minute) and every 6 hours.</summary>
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    /// <summary>"Auto" mode waits for a quiet moment to restart; retried while K-Desktop is in use.</summary>
    private readonly DispatcherTimer _autoInstallRetry = new() { Interval = TimeSpan.FromMinutes(2) };

    private UpdateState _lastUpdateState;
    private Version? _notifiedVersion;

    public UpdateService Updates { get; } = new();

    /// <summary>Version this run was updated from (it was started by the previous one after installing), or null.</summary>
    public string? UpdatedFrom { get; init; }

    private void InitUpdates()
    {
        Updates.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            if (Updates.State == _lastUpdateState) return; // progress only
            _lastUpdateState = Updates.State;
            _main?.ShowBanners();
        });

        if (UpdatedFrom is not null)
        {
            Log.Info($"Actualizado desde la versión {UpdatedFrom}");
            Ui.ShowOsd(Loc.T("updates.updated", UpdateService.CurrentVersion.ToString(3)), OsdKind.Success, TimeSpan.FromSeconds(5));
        }
        else Task.Run(UpdateService.CleanUp); // exes left by a publish or an update that could not be removed then

        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            _ = CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Interval = TimeSpan.FromMinutes(1);
        _updateTimer.Start();
        _autoInstallRetry.Tick += (_, _) => TryAutoInstall();
    }

    /// <summary>Checks the manifest; in "Auto" mode also downloads the new version and installs it when idle.</summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        var s = Config.Settings;
        if (!manual && s.UpdateMode == UpdateMode.Off) return;
        var info = await Updates.CheckAsync(s.UpdateUrl);
        if (info is null) return;

        if (s.UpdateMode == UpdateMode.Auto && UpdateService.CanInstall && !manual)
        {
            if (await Updates.DownloadAsync() is not null) TryAutoInstall();
            return;
        }
        if (!manual && _notifiedVersion != info.Version)
        {
            _notifiedVersion = info.Version;
            Ui.ShowOsd(Loc.T("updates.osdAvailable", info.Version.ToString(3)), OsdKind.Info, TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Restarting closes everything for a couple of seconds, so "Auto" waits until nobody is using K-Desktop: the
    /// window closed and nothing running (macros, countdowns, voice, QuickMenus).
    /// </summary>
    private void TryAutoInstall()
    {
        _autoInstallRetry.Stop();
        if (Config.Settings.UpdateMode != UpdateMode.Auto || Updates.State != UpdateState.Ready || _exiting) return;
        var busy = _main is { IsVisible: true } || Engine.HasCancellable || Engine.IsPlayingMacro || Voice.IsListening || QuickMenus.IsOpen;
        if (busy)
        {
            _autoInstallRetry.Start();
            return;
        }
        _ = InstallUpdateAsync();
    }

    /// <summary>Downloads (if needed) and installs the available version, then restarts into it.</summary>
    public async Task InstallUpdateAsync()
    {
        if (Updates.Available is not { } info || _exiting) return;
        if (!UpdateService.CanInstall)
        {
            ProcessLauncher.Launch(new LaunchSpec(info.Download.ToString()), asAdmin: false);
            return;
        }
        if (Updates.State != UpdateState.Ready && await Updates.DownloadAsync() is null)
        {
            if (Updates.Error is { } error) Ui.ShowOsd(Loc.T("updates.installError", error), OsdKind.Warning, TimeSpan.FromSeconds(5));
            return;
        }

        try
        {
            Store.Flush();
            Achievements.Flush();
            Engine.History.Save();
            Updates.Install(RestartArguments());
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo instalar la actualización", ex);
            Ui.ShowOsd(Loc.T("updates.installError", ex.Message), OsdKind.Warning, TimeSpan.FromSeconds(5));
            return;
        }

        // Not ExitAsync: an update restart is not "leaving", so no exit sound or "run on exit" commands.
        _exiting = true;
        QuickMenus.CloseAll(restoreFocus: false);
        _main?.Close();
        Shutdown();
    }

    /// <summary>What the new instance needs to run like this one: the data folder, the --no-* switches, the window.</summary>
    private List<string> RestartArguments()
    {
        var args = new List<string>();
        if (Program.ValueOf(Arguments, "--data") is { } data) args.AddRange(["--data", AppPaths.DataDir]);
        foreach (var flag in (string[])["--no-hooks", "--no-udp", "--no-web", "--no-elevate"])
            if (Arguments.Contains(flag, StringComparer.OrdinalIgnoreCase)) args.Add(flag);
        if (!ProcessLauncher.IsElevated && !args.Contains("--no-elevate")) args.Add("--no-elevate"); // no UAC prompt out of the blue
        if (_main is { IsVisible: true }) args.Add("--show");
        return args;
    }
}
