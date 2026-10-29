using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>
/// «Disposiciones» (E1, 5.0.375; zones since E1b, 5.0.376): how the monitors are cut into zones and which programs go in
/// each one. It covers every monitor; one without zones in it is not touched when it is put. Put with the action
/// «Disposición de ventanas»: each program to its zone; optionally, the programs that are not open are opened.
/// </summary>
public partial class WindowArrangement : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private DateTime _created = DateTime.Now;

    /// <summary>A program of a zone that is not open is opened (from its path) and placed when its window appears.</summary>
    [ObservableProperty] private bool _openMissing;

    public ObservableCollection<ArrangedZone> Zones
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CollectionChanged -= OnZonesChanged;
                foreach (var z in field) z.Modified -= OnZoneModified;
            }
            field = value ?? [];
            field.CollectionChanged += OnZonesChanged;
            foreach (var z in field) z.Modified += OnZoneModified;
        }
    } = [];

    /// <summary>Until 5.0.375 a disposición was a list of windows: only read, <see cref="ConvertWindows"/> makes them zones.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ArrangedWindow>? Windows { get; set; }

    public WindowArrangement() => Zones.CollectionChanged += OnZonesChanged;

    /// <summary>«3 zonas · 4 programas», for the list and the action.</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (Zones.Count == 0) return Loc.T("arrangement.noZones");
            var programs = Zones.Sum(z => z.Programs.Count);
            return Loc.T(Zones.Count == 1 ? "arrangement.zonesOne" : "arrangement.zonesMany", Zones.Count) + "  ·  "
                   + Loc.T(programs == 1 ? "arrangement.programsOne" : "arrangement.programsMany", programs);
        }
    }

    /// <summary>Raised whenever it changes (autosave).</summary>
    public event EventHandler? Modified;

    /// <summary>Re-evaluates the localised texts (after a language change).</summary>
    public void RefreshTexts() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnZonesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ArrangedZone z in e.OldItems) z.Modified -= OnZoneModified;
        if (e.NewItems is not null)
            foreach (ArrangedZone z in e.NewItems) z.Modified += OnZoneModified;
        OnZoneModified(sender, EventArgs.Empty);
    }

    private void OnZoneModified(object? sender, EventArgs e)
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The windows of a disposición of 5.0.375 as zones: each one a zone of its monitor with its program (a maximised one,
    /// the whole screen); two in the same place share the zone. One that was switched off keeps its zone without program.
    /// </summary>
    public bool ConvertWindows()
    {
        if (Windows is not { } windows) return false;
        foreach (var w in windows)
        {
            var (left, top, width, height) = w.State == ArrangedState.Maximized ? (0d, 0d, 1d, 1d) : (w.Left, w.Top, w.Width, w.Height);
            var zone = Zones.FirstOrDefault(z => z.IsOn(w.MonitorName, w.MonitorDevice) && z.SameRect(left, top, width, height));
            if (zone is null)
            {
                zone = new ArrangedZone { MonitorName = w.MonitorName, MonitorDevice = w.MonitorDevice };
                zone.SetRect(left, top, width, height);
                Zones.Add(zone);
            }
            if (w.Enabled && w.Process.Length > 0) zone.Programs.Add(new ZoneProgram { Process = w.Process, Name = w.Name, Title = w.Title, Path = w.Path });
        }
        Windows = null;
        return true;
    }

    public WindowArrangement Duplicate() => new()
    {
        Name = Name,
        Notes = Notes,
        OpenMissing = OpenMissing,
        Zones = [.. Zones.Select(z => z.Clone())],
    };
}

/// <summary>
/// One zone of a monitor: its monitor (by its own name, with its GDI name to tell two equal ones apart), its place in
/// fractions of the monitor's work area (0…1, so it fits another resolution) and the programs that go in it. Without
/// programs it is only there to snap windows into.
/// </summary>
public partial class ArrangedZone : ObservableObject
{
    /// <summary>The monitor's own name («DELL U2723QE»), how it is found again.</summary>
    [ObservableProperty] private string _monitorName = string.Empty;

    /// <summary>The monitor's GDI name («\\.\DISPLAY2»).</summary>
    [ObservableProperty] private string _monitorDevice = string.Empty;

    [ObservableProperty] private double _left;
    [ObservableProperty] private double _top;
    [ObservableProperty] private double _width = 1;
    [ObservableProperty] private double _height = 1;

    public ObservableCollection<ZoneProgram> Programs
    {
        get;
        set
        {
            if (field is not null) field.CollectionChanged -= OnProgramsChanged;
            field = value ?? [];
            field.CollectionChanged += OnProgramsChanged;
        }
    } = [];

    public ArrangedZone() => Programs.CollectionChanged += OnProgramsChanged;

    /// <summary>Raised whenever it changes.</summary>
    public event EventHandler? Modified;

    [JsonIgnore] public double Right => Left + Width;
    [JsonIgnore] public double Bottom => Top + Height;

    /// <summary>It covers the whole monitor: a window put in it is maximised.</summary>
    [JsonIgnore] public bool IsFull => Left <= 0.01 && Top <= 0.01 && Width >= 0.98 && Height >= 0.98;

    /// <summary>«Google Chrome, Code +1», inside the zone in the drawing.</summary>
    [JsonIgnore]
    public string Label => Programs.Count switch
    {
        0 => string.Empty,
        <= 2 => string.Join(", ", Programs.Select(p => p.Display)),
        _ => string.Join(", ", Programs.Take(2).Select(p => p.Display)) + " +" + (Programs.Count - 2).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>«50 × 100 %», its size.</summary>
    [JsonIgnore]
    public string Size => string.Format(CultureInfo.InvariantCulture, "{0:0} × {1:0} %", Width * 100, Height * 100);

    public void SetRect(double left, double top, double width, double height)
    {
        Left = Math.Round(left, 4);
        Top = Math.Round(top, 4);
        Width = Math.Round(width, 4);
        Height = Math.Round(height, 4);
    }

    public bool SameRect(double left, double top, double width, double height) =>
        Math.Abs(Left - left) < 0.01 && Math.Abs(Top - top) < 0.01 && Math.Abs(Width - width) < 0.01 && Math.Abs(Height - height) < 0.01;

    /// <summary>It is on that monitor: the same own name and, when both have it, the same GDI name.</summary>
    public bool IsOn(string name, string device) =>
        MonitorName.Equals(name, StringComparison.OrdinalIgnoreCase) && (MonitorDevice.Length == 0 || device.Length == 0 || MonitorDevice.Equals(device, StringComparison.OrdinalIgnoreCase));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Width) or nameof(Height)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Size)));
        if (e.PropertyName is not (nameof(Label) or nameof(Size))) Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnProgramsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Label)));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    public ArrangedZone Clone() => new()
    {
        MonitorName = MonitorName, MonitorDevice = MonitorDevice, Left = Left, Top = Top, Width = Width, Height = Height,
        Programs = [.. Programs.Select(p => p.Clone())],
    };
}

/// <summary>A program of a zone: its process, its name, the title its window had (to tell apart several windows of the same one) and its file.</summary>
public sealed class ZoneProgram
{
    /// <summary>The process, in lowercase without .exe («chrome»).</summary>
    public string Process { get; set; } = string.Empty;

    /// <summary>The name to show («Google Chrome»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The title of its window when it was saved (empty: any of its windows).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The program's file, to open it when it is not open (empty: it cannot be opened, like a Store app).</summary>
    public string Path { get; set; } = string.Empty;

    [JsonIgnore] public string Display => Name.Length > 0 ? Name : Process;

    /// <summary>Its chip's tooltip: the title of its window, if it has one.</summary>
    [JsonIgnore] public string Tip => Title.Length > 0 ? $"{Display} · {Title}" : Display;

    public ZoneProgram Clone() => new() { Process = Process, Name = Name, Title = Title, Path = Path };
}

/// <summary>How a window of a disposición of 5.0.375 was left (only read, to convert it).</summary>
public enum ArrangedState { Normal, Maximized, Minimized }

/// <summary>One window of a disposición of 5.0.375 (only read: <see cref="WindowArrangement.ConvertWindows"/> makes it a zone).</summary>
public sealed class ArrangedWindow
{
    public string Process { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string MonitorName { get; set; } = string.Empty;
    public string MonitorDevice { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 0.5;
    public double Height { get; set; } = 1;
    public ArrangedState State { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// A monitor K-Desktop has seen (E1b): its own name, its GDI name, its resolution and its work area, so a disposición can
/// still be cut for it when it is not connected.
/// </summary>
public sealed class KnownMonitor
{
    public string Name { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int WorkWidth { get; set; }
    public int WorkHeight { get; set; }
}

/// <summary>The disposición put last on a monitor (E1b): the one its windows snap into.</summary>
public sealed class ActiveArrangement
{
    public string MonitorName { get; set; } = string.Empty;
    public string MonitorDevice { get; set; } = string.Empty;
    public string ArrangementId { get; set; } = string.Empty;
}
