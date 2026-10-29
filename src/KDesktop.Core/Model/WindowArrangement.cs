using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>How a window of a disposición is left.</summary>
public enum ArrangedState { Normal, Maximized, Minimized }

/// <summary>
/// «Disposiciones» (E1, 5.0.375): where some windows go, saved as they were («Guardar cómo están ahora») and put back with
/// the action «Disposición de ventanas»: each program on its monitor (by its name; one that is not there, on the main one),
/// in the same place in proportion and the same state. Optionally, the programs that are not open are opened.
/// </summary>
public partial class WindowArrangement : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private DateTime _created = DateTime.Now;

    /// <summary>A program of the list that is not open is opened (from its path) and placed when its window appears.</summary>
    [ObservableProperty] private bool _openMissing;

    public ObservableCollection<ArrangedWindow> Windows
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CollectionChanged -= OnWindowsChanged;
                foreach (var w in field) w.PropertyChanged -= OnWindowChanged;
            }
            field = value ?? [];
            field.CollectionChanged += OnWindowsChanged;
            foreach (var w in field) w.PropertyChanged += OnWindowChanged;
        }
    } = [];

    public WindowArrangement() => Windows.CollectionChanged += OnWindowsChanged;

    /// <summary>«4 ventanas», for the list and the action.</summary>
    [JsonIgnore]
    public string Summary => Windows.Count == 0 ? Loc.T("arrangement.noWindows")
        : Loc.T(Windows.Count == 1 ? "arrangement.windowsOne" : "arrangement.windowsMany", Windows.Count);

    /// <summary>Raised whenever it changes (autosave).</summary>
    public event EventHandler? Modified;

    /// <summary>Re-evaluates the localised texts (after a language change).</summary>
    public void RefreshTexts()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        foreach (var w in Windows) w.RefreshTexts();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ArrangedWindow w in e.OldItems) w.PropertyChanged -= OnWindowChanged;
        if (e.NewItems is not null)
            foreach (ArrangedWindow w in e.NewItems) w.PropertyChanged += OnWindowChanged;
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Its texts follow what changed: only that is saved.
        if (e.PropertyName is not (nameof(ArrangedWindow.Where) or nameof(ArrangedWindow.Exact))) Modified?.Invoke(this, EventArgs.Empty);
    }

    public WindowArrangement Duplicate() => new()
    {
        Name = Name,
        Notes = Notes,
        OpenMissing = OpenMissing,
        Windows = [.. Windows.Select(w => w.Clone())],
    };
}

/// <summary>
/// One window of a disposición: its program, its title when it was saved (to tell apart several windows of the same one),
/// its monitor and where on it, in fractions of the monitor's work area (0…1, so it fits another resolution).
/// </summary>
public partial class ArrangedWindow : ObservableObject
{
    /// <summary>The process, in lowercase without .exe («chrome»).</summary>
    [ObservableProperty] private string _process = string.Empty;

    /// <summary>The name to show («Google Chrome»).</summary>
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private string _title = string.Empty;

    /// <summary>The program's file, to open it when it is not open (empty: it cannot be opened, like a Store app).</summary>
    [ObservableProperty] private string _path = string.Empty;

    /// <summary>The monitor's own name («DELL U2723QE»), how it is found again; <see cref="MonitorDevice"/> tells two equal ones apart.</summary>
    [ObservableProperty] private string _monitorName = string.Empty;

    /// <summary>The monitor's GDI name («\\.\DISPLAY2»).</summary>
    [ObservableProperty] private string _monitorDevice = string.Empty;

    [ObservableProperty] private double _left;
    [ObservableProperty] private double _top;
    [ObservableProperty] private double _width = 0.5;
    [ObservableProperty] private double _height = 1;

    [ObservableProperty] private ArrangedState _state;

    [ObservableProperty] private bool _enabled = true;

    /// <summary>«DELL U2723QE · izquierda · maximizada», under its name.</summary>
    [JsonIgnore]
    public string Where
    {
        get
        {
            var parts = new List<string>();
            if (MonitorName.Length > 0) parts.Add(MonitorName);
            if (State != ArrangedState.Normal) parts.Add(Loc.T("arrangement.state." + State));
            else parts.Add(Loc.T("arrangement.place." + Place(Left, Top, Width, Height)));
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Where on its monitor, by its middle, as list_windows says it («left», «topRight», «full»…).</summary>
    public static string Place(double left, double top, double width, double height)
    {
        if (width >= 0.9 && height >= 0.9) return "full";
        var column = width >= 0.8 ? 1 : left + width / 2 < 0.4 ? 0 : left + width / 2 > 0.6 ? 2 : 1;
        var row = height >= 0.8 ? 1 : top + height / 2 < 0.4 ? 0 : top + height / 2 > 0.6 ? 2 : 1;
        return (column, row) switch
        {
            (0, 0) => "topLeft",
            (2, 0) => "topRight",
            (0, 2) => "bottomLeft",
            (2, 2) => "bottomRight",
            (0, _) => "left",
            (2, _) => "right",
            (_, 0) => "top",
            (_, 2) => "bottom",
            _ => "center",
        };
    }

    /// <summary>«32 %, 0 % · 50 × 100 %», the exact place (its tooltip).</summary>
    [JsonIgnore]
    public string Exact => string.Format(CultureInfo.InvariantCulture, "{0:0}%, {1:0}%  ·  {2:0} × {3:0}%", Left * 100, Top * 100, Width * 100, Height * 100);

    public void RefreshTexts() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Where)));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(MonitorName) or nameof(State) or nameof(Left) or nameof(Top) or nameof(Width) or nameof(Height))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Where)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Exact)));
        }
    }

    public ArrangedWindow Clone() => new()
    {
        Process = Process, Name = Name, Title = Title, Path = Path, MonitorName = MonitorName, MonitorDevice = MonitorDevice,
        Left = Left, Top = Top, Width = Width, Height = Height, State = State, Enabled = Enabled,
    };
}
