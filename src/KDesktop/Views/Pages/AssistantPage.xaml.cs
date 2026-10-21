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
/// memory, processor and the last error), with a restart and its log, and the assistant's own cards (its panel.json,
/// stage 1.9). With several on the desktop (stage 1.11), pills on top choose whose card and panel are shown
/// (<c>--page assistant/&lt;id&gt;</c>). The conversation and the activity log come later.
/// </summary>
public partial class AssistantPage : UserControl
{
    /// <summary>The states of the <c>status</c> message (protocolo.md).</summary>
    private static readonly HashSet<string> States = ["idle", "listening", "thinking", "speaking", "awaitingConfirmation", "busy", "sleeping"];

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _usage = new() { Interval = TimeSpan.FromSeconds(2) };
    private (int Pid, TimeSpan Cpu, DateTime At)? _lastCpu;
    /// <summary>The assistant whose card is shown (one on the desktop; null: nobody there).</summary>
    private string? _shown;

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

        // Who is on the desktop, and whose card is shown
        var onDesktop = _app.AssistantsOnDesktop.Where(i => AssistantPackages.Find(i) is not null).ToList();
        if (_shown is null || !onDesktop.Contains(_shown)) _shown = onDesktop.FirstOrDefault();
        FillPickers(onDesktop);
        var active = _shown is null ? null : AssistantPackages.Find(_shown);
        HeroName.Text = active?.Manifest.Name ?? Loc.T("assistantPanel.nobody");
        HeroByline.Text = active is null ? Loc.T("assistantPanel.nobodyHint") : Loc.T("assistantCard.by", active.Version, active.Manifest.Author);
        var icon = active is null ? null : AssistantLook.Icon(active);
        HeroIcon.Source = icon;
        HeroFallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        var (state, brush) = active is null ? AssistantLook.Summary(_app) : AssistantLook.Summary(_app, active.Id);
        HeroState.Text = state;
        HeroDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        HideButton.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = active is not null && AssistantLook.HasSettings(active) ? Visibility.Visible : Visibility.Collapsed;
        OwnPanel.Visibility = OwnPanel.Show(active) ? Visibility.Visible : Visibility.Collapsed;
        Chat.Show(active?.Id, active?.Manifest.Name ?? "K-Desktop");
        // With somebody there, the way to put another one next to it.
        ChooseButton.Content = Loc.T(active is null ? "assistantPanel.choose" : "assistantPanel.addAnother");

        // The process and what the assistant says of itself (its own status, or the process's)
        var status = (_shown is null ? null : _app.AssistantStateOf(_shown)) ?? _app.AssistantStatus;
        ActivityText.Text = status is null ? "—" : States.Contains(status.State) ? Loc.T("assistantPanel.state." + status.State) : status.State;
        var hasModel = status?.Model is { Length: > 0 };
        ModelLabel.Visibility = ModelText.Visibility = hasModel ? Visibility.Visible : Visibility.Collapsed;
        if (hasModel)
            ModelText.Text = status!.ModelLoaded == true ? Loc.T("assistantPanel.modelLoaded", status.Model) : Loc.T("assistantPanel.modelNotLoaded", status.Model);
        ErrorPanel.Visibility = _app.AssistantError is null ? Visibility.Collapsed : Visibility.Visible;
        if (_app.AssistantError is { } error) ErrorText.Text = $"{error.Text} ({error.At.ToString("g", Loc.Instance.Culture)})";
        UpdateUsage();
    }

    /// <summary>Shows this assistant's card and panel (<c>--page assistant/&lt;id&gt;</c>, its menu → Panel).</summary>
    public void Show(string id)
    {
        _shown = id;
        if (IsLoaded) Refresh();
    }

    /// <summary>One pill per assistant on the desktop, only when there is more than one.</summary>
    private void FillPickers(List<string> onDesktop)
    {
        Pickers.Children.Clear();
        Pickers.Visibility = onDesktop.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (onDesktop.Count < 2) return;
        foreach (var id in onDesktop)
        {
            var name = App.AssistantName(id);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (AssistantPackages.Find(id) is { } installed && AssistantLook.Icon(installed) is { } icon)
                content.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(-4, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var pill = new RadioButton
            {
                Style = (Style)FindResource("Pill"),
                GroupName = "AssistantShown",
                IsChecked = id == _shown,
                Content = content,
            };
            System.Windows.Automation.AutomationProperties.SetName(pill, name);
            pill.Checked += (_, _) =>
            {
                if (_shown == id) return;
                _shown = id;
                Dispatcher.BeginInvoke(Refresh);
            };
            Pickers.Children.Add(pill);
        }
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

    private void OnHide(object sender, RoutedEventArgs e)
    {
        if (_shown is { } id) _app.HideAssistant(id);
    }

    private void OnAssistantSettings(object sender, RoutedEventArgs e)
    {
        if (_shown is { } id) (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistants/" + id);
    }

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
