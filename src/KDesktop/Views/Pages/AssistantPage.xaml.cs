using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Views.Pages;

/// <summary>
/// Panel → Assistant (stage 1.8, §13.2): who is on the desktop and how the assistant process is doing (its state,
/// memory, processor and the last error), with a restart and its log. The conversation, the activity log and the
/// assistant's own panel come later.
/// </summary>
public partial class AssistantPage : UserControl
{
    /// <summary>The states of the <c>status</c> message (protocolo.md).</summary>
    private static readonly HashSet<string> States = ["idle", "listening", "thinking", "speaking", "awaitingConfirmation", "busy", "sleeping"];

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _usage = new() { Interval = TimeSpan.FromSeconds(2) };
    private (int Pid, TimeSpan Cpu, DateTime At)? _lastCpu;

    public AssistantPage()
    {
        InitializeComponent();
        _usage.Tick += (_, _) => UpdateUsage();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            Refresh();
            _usage.Start();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= Refresh;
            _usage.Stop();
        };
    }

    private void Refresh()
    {
        var on = _app.Assistant is not null;
        OffPanel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        OnPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;

        // Who is on the desktop
        var active = _app.Config.Settings.AssistantActive is { } id ? AssistantPackages.Find(id) : null;
        HeroName.Text = active?.Manifest.Name ?? Loc.T("assistantPanel.nobody");
        HeroByline.Text = active is null ? Loc.T("assistantPanel.nobodyHint") : Loc.T("assistantCard.by", active.Version, active.Manifest.Author);
        var icon = active is null ? null : AssistantLook.Icon(active);
        HeroIcon.Source = icon;
        HeroFallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        var (state, brush) = AssistantLook.Summary(_app);
        HeroState.Text = state;
        HeroDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        HideButton.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        ChooseButton.Content = Loc.T(active is null ? "assistantPanel.choose" : "assistantPanel.change");

        // The process and what the assistant says of itself
        var status = _app.AssistantStatus;
        ActivityText.Text = status is null ? "—" : States.Contains(status.State) ? Loc.T("assistantPanel.state." + status.State) : status.State;
        var hasModel = status?.Model is { Length: > 0 };
        ModelLabel.Visibility = ModelText.Visibility = hasModel ? Visibility.Visible : Visibility.Collapsed;
        if (hasModel)
            ModelText.Text = status!.ModelLoaded == true ? Loc.T("assistantPanel.modelLoaded", status.Model) : Loc.T("assistantPanel.modelNotLoaded", status.Model);
        ErrorPanel.Visibility = _app.AssistantError is null ? Visibility.Collapsed : Visibility.Visible;
        if (_app.AssistantError is { } error) ErrorText.Text = $"{error.Text} ({error.At.ToString("g", Loc.Instance.Culture)})";
        UpdateUsage();
    }

    private void OnTechExpanded(object sender, RoutedEventArgs e) => UpdateUsage();

    /// <summary>
    /// The process line, its memory and its processor use: every two seconds while the page is shown and the
    /// technical details are open.
    /// </summary>
    private void UpdateUsage()
    {
        if (_app.Assistant is not { } supervisor || !TechExpander.IsExpanded) return;
        var pid = supervisor.ProcessId;
        ProcessText.Text = supervisor.State switch
        {
            AssistantProcessState.Running when _app.AssistantConnectedAt is { } since =>
                Loc.T("assistantPanel.running", pid, Elapsed(DateTime.Now - since)),
            AssistantProcessState.Running or AssistantProcessState.Starting => Loc.T("assistantState.starting"),
            AssistantProcessState.Restarting => Loc.T("assistantState.restarting"),
            _ => Loc.T("assistantState.stopped"),
        };
        RestartButton.IsEnabled = supervisor.State == AssistantProcessState.Running;

        string memory = "—", cpu = "—";
        if (pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                memory = Loc.T("assistantPanel.memoryValue", Megabytes(process.WorkingSet64), Megabytes(process.PrivateMemorySize64));
                var now = DateTime.UtcNow;
                var used = process.TotalProcessorTime;
                if (_lastCpu is { } last && last.Pid == pid && now > last.At)
                {
                    var percent = (used - last.Cpu).TotalMilliseconds / (now - last.At).TotalMilliseconds / Environment.ProcessorCount * 100;
                    cpu = Math.Max(0, percent).ToString("0.0", Loc.Instance.Culture) + " %";
                }
                _lastCpu = (pid, used, now);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _lastCpu = null; // it just ended
            }
        }
        MemoryText.Text = memory;
        CpuText.Text = cpu;
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("N0", Loc.Instance.Culture) + " MB";

    private static string Elapsed(TimeSpan time) =>
        time.TotalMinutes < 1 ? Loc.T("assistantPanel.seconds", (int)time.TotalSeconds)
        : time.TotalHours < 1 ? Loc.T("assistantPanel.minutes", (int)time.TotalMinutes)
        : Loc.T("assistantPanel.hours", (int)time.TotalHours, time.Minutes);

    private void OnTurnOn(object sender, RoutedEventArgs e) => _app.Config.Settings.AssistantEnabled = true;

    private void OnHide(object sender, RoutedEventArgs e) => _app.ActivateAssistant(null);

    private void OnChoose(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistants");

    private void OnOptions(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistant");

    private void OnRestart(object sender, RoutedEventArgs e) => _app.RestartAssistantProcess();

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        var log = Path.Combine(AppPaths.LogDir, "assistant.log");
        if (File.Exists(log)) ProcessLauncher.Launch(new LaunchSpec(log), asAdmin: false);
        else _app.Ui.ShowOsd(Loc.T("assistantPanel.noLog"), Core.Engine.OsdKind.Info);
    }
}
