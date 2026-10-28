using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Views;

/// <summary>
/// The monitor, resolution and refresh rate of a «Pantalla › Resolución» action (its DataContext). Choosing a monitor
/// proposes the mode it has now; the lists mark the native resolution and the current one. «Sin cambiar» (0) keeps that
/// part as it is.
/// </summary>
public partial class DisplayModeFields : UserControl
{
    private sealed record Choice(int Width, int Height, int Frequency, string Name)
    {
        public override string ToString() => Name;
    }

    private bool _filling;

    public DisplayModeFields()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Fill();
        // Monitors come and go: read them again each time the list opens.
        MonitorBox.DropDownOpened += (_, _) => FillMonitors();
    }

    private DisplayAction? Model => DataContext as DisplayAction;

    private void Fill()
    {
        FillMonitors();
        FillModes();
    }

    private void FillMonitors()
    {
        if (Model is not { } a) return;
        var monitors = DisplayTools.Monitors();
        Quietly(() =>
        {
            MonitorBox.ItemsSource = monitors;
            MonitorBox.SelectedItem = monitors.FirstOrDefault(m => m.DeviceName == a.Monitor);
        });
    }

    private void FillModes()
    {
        if (Model is not { } a || a.Monitor.Length == 0)
        {
            Quietly(() => ResolutionBox.ItemsSource = FrequencyBox.ItemsSource = null);
            return;
        }
        var modes = DisplayTools.Modes(a.Monitor);
        var current = DisplayTools.CurrentMode(a.Monitor);
        // The modes come best first: the biggest one is the panel's own.
        var native = modes.Count > 0 ? modes[0] : null;
        var keep = Loc.T("editor.modeKeep");

        var resolutions = new List<Choice> { new(0, 0, 0, keep) };
        foreach (var (w, h) in modes.Select(m => (m.Width, m.Height)).Distinct())
            resolutions.Add(new Choice(w, h, 0, Size(w, h) + Tags(native is { } n && n.Width == w && n.Height == h, current is { } c && c.Width == w && c.Height == h)));

        Quietly(() =>
        {
            ResolutionBox.ItemsSource = resolutions;
            ResolutionBox.SelectedItem = resolutions.FirstOrDefault(r => r.Width == a.Width && r.Height == a.Height) ?? resolutions[0];
        });
        FillFrequencies(modes, current);
    }

    /// <summary>The rates of the chosen resolution (of the current one with «Sin cambiar»).</summary>
    private void FillFrequencies(IReadOnlyList<DisplayModeInfo> modes, DisplayModeInfo? current)
    {
        if (Model is not { } a) return;
        var (w, h) = a.Width > 0 ? (a.Width, a.Height) : current is { } c ? (c.Width, c.Height) : (0, 0);
        var rates = new List<Choice> { new(0, 0, 0, Loc.T("editor.modeKeep")) };
        foreach (var f in modes.Where(m => m.Width == w && m.Height == h).Select(m => m.Frequency).Distinct().OrderDescending())
            rates.Add(new Choice(0, 0, f, string.Create(CultureInfo.InvariantCulture, $"{f} Hz") +
                Tags(false, current is { } now && now.Width == w && now.Height == h && now.Frequency == f)));
        Quietly(() =>
        {
            FrequencyBox.ItemsSource = rates;
            FrequencyBox.SelectedItem = rates.FirstOrDefault(r => r.Frequency == a.Frequency) ?? rates[0];
        });
    }

    private static string Size(int w, int h) => string.Create(CultureInfo.InvariantCulture, $"{w} × {h}");

    private static string Tags(bool native, bool current)
    {
        var tags = new List<string>(2);
        if (native) tags.Add(Loc.T("editor.modeNative"));
        if (current) tags.Add(Loc.T("editor.modeCurrent"));
        return tags.Count == 0 ? "" : "  ·  " + string.Join(", ", tags);
    }

    private void OnMonitorPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Model is not { } a || MonitorBox.SelectedItem is not MonitorInfo monitor || monitor.DeviceName == a.Monitor) return;
        a.Monitor = monitor.DeviceName;
        a.MonitorName = monitor.Name;
        // What it has now: from there one changes what one wants.
        if (DisplayTools.CurrentMode(monitor.DeviceName) is { } mode)
        {
            a.Width = mode.Width;
            a.Height = mode.Height;
            a.Frequency = mode.Frequency;
        }
        FillModes();
    }

    private void OnResolutionPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Model is not { } a || ResolutionBox.SelectedItem is not Choice r) return;
        a.Width = r.Width;
        a.Height = r.Height;
        var modes = DisplayTools.Modes(a.Monitor);
        var current = DisplayTools.CurrentMode(a.Monitor);
        // A rate the new resolution doesn't have: the best one it has.
        var (w, h) = r.Width > 0 ? (r.Width, r.Height) : current is { } c ? (c.Width, c.Height) : (0, 0);
        if (a.Frequency > 0 && !modes.Any(m => m.Width == w && m.Height == h && m.Frequency == a.Frequency))
            a.Frequency = modes.Where(m => m.Width == w && m.Height == h).Select(m => m.Frequency).DefaultIfEmpty(0).Max();
        FillFrequencies(modes, current);
    }

    private void OnFrequencyPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || Model is not { } a || FrequencyBox.SelectedItem is not Choice f) return;
        a.Frequency = f.Frequency;
    }

    private void Quietly(Action change)
    {
        _filling = true;
        try { change(); }
        finally { _filling = false; }
    }
}
