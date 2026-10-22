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
/// Panel → Assistant (stage 1.8, §13.2): the assistants in a column, as the lists of the other pages (5.0.112: K-Desktop
/// first, those on the desktop marked), and the one chosen (<c>--page assistant/&lt;id&gt;</c>): its card with putting it
/// there or hiding it, the conversation with it, its own cards (its panel.json, stage 1.9), how the assistant process is
/// doing (its state, memory, processor and the last error), with a restart and its log, and the activity.
/// </summary>
public partial class AssistantPage : UserControl
{
    /// <summary>The states of the <c>status</c> message (protocolo.md).</summary>
    private static readonly HashSet<string> States = ["idle", "listening", "thinking", "speaking", "awaitingConfirmation", "busy", "sleeping"];

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _usage = new() { Interval = TimeSpan.FromSeconds(2) };
    private (int Pid, TimeSpan Cpu, DateTime At)? _lastCpu;
    /// <summary>The assistant whose card is shown (the one chosen in the list).</summary>
    private string? _shown;
    /// <summary>What the list shows (ids, names and where each one is): it is made again only when that changes.</summary>
    private string? _listShown;
    private bool _filling;

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

        // The list, and whose card is shown: the one chosen, else the first one on the desktop, else K-Desktop.
        var rows = AssistantLook.Rows(_app);
        if (_shown is null || rows.All(r => r.Id != _shown)) _shown = (rows.FirstOrDefault(r => r.IsOnDesktop) ?? rows[0]).Id;
        FillList(rows);
        var row = rows.First(r => r.Id == _shown);
        HeroName.Text = row.Name;
        HeroByline.Text = row.Byline;
        HeroIcon.Source = row.Icon;
        HeroFallback.Visibility = row.NoIcon ? Visibility.Visible : Visibility.Collapsed;
        var (state, brush) = AssistantLook.Summary(_app, row.Id);
        HeroState.Text = state;
        HeroDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        HideButton.Content = row.HideText;
        PutButton.Content = row.PutText;
        HideButton.Visibility = row.IsOnDesktop ? Visibility.Visible : Visibility.Collapsed;
        PutButton.Visibility = row.IsOnDesktop ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = row.HasSettings ? Visibility.Visible : Visibility.Collapsed;
        OwnPanel.Visibility = OwnPanel.Show(row.Installed) ? Visibility.Visible : Visibility.Collapsed;
        Chat.Show(row.Id, row.Name, row.IsOnDesktop);
        var active = rows.Count(r => r.IsOnDesktop);
        CountText.Text = active.ToString(Loc.Instance.Culture);
        CountChip.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The process and what the assistant says of itself (its own status, or the process's)
        var status = (_shown is null ? null : _app.AssistantStateOf(_shown)) ?? _app.AssistantStatus;
        ActivityText.Text = status is null ? "—" : States.Contains(status.State) ? Loc.T("assistantPanel.state." + status.State) : status.State;
        var hasModel = status?.Model is { Length: > 0 };
        ModelLabel.Visibility = ModelText.Visibility = hasModel ? Visibility.Visible : Visibility.Collapsed;
        if (hasModel)
        {
            var label = Core.Assistant.Brain.BrainProviders.ModelLabel(status!.Model!);
            ModelText.Text = status.ModelLoaded == true ? Loc.T("assistantPanel.modelLoaded", label) : Loc.T("assistantPanel.modelNotLoaded", label);
        }
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

    /// <summary>The assistants in the list, with the one shown chosen; made again only when something in it changed.</summary>
    private void FillList(List<AssistantRow> rows)
    {
        var key = string.Join("|", rows.Select(r => $"{r.Id}:{r.Name}:{r.StateText}")) + ">" + _shown;
        if (key == _listShown) return;
        _listShown = key;
        _filling = true;
        AssistantList.ItemsSource = rows;
        AssistantList.SelectedItem = rows.FirstOrDefault(r => r.Id == _shown);
        _filling = false;
    }

    private void OnAssistantSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || AssistantList.SelectedItem is not AssistantRow row || row.Id == _shown) return;
        _shown = row.Id;
        Dispatcher.BeginInvoke(Refresh);
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

    private void OnPut(object sender, RoutedEventArgs e)
    {
        if (_shown is { } id) _app.ShowAssistant(id);
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
