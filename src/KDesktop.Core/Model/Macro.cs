using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

public enum MacroStepKind
{
    KeyDown, KeyUp, KeyPress, Text,
    MouseDown, MouseUp, Click, MouseMove, Wheel,
}

/// <summary>A recorded (or hand-written) sequence of keyboard and mouse events with their timing.</summary>
public partial class Macro : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private DateTime _created = DateTime.Now;

    public ObservableCollection<MacroStep> Steps
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CollectionChanged -= OnStepsChanged;
                foreach (var s in field) s.PropertyChanged -= OnStepChanged;
            }
            field = value ?? [];
            field.CollectionChanged += OnStepsChanged;
            foreach (var s in field) s.PropertyChanged += OnStepChanged;
            OnStepsChanged(field, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    } = [];

    public Macro() => Steps.CollectionChanged += OnStepsChanged;

    /// <summary>Replaces every step at once (one notification instead of one per step).</summary>
    public void ReplaceSteps(IEnumerable<MacroStep> steps)
    {
        Steps = new ObservableCollection<MacroStep>(steps);
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Steps)));
    }

    /// <summary>Raised whenever the macro or any of its steps changes (autosave).</summary>
    public event EventHandler? Modified;

    /// <summary>Total playing time at normal speed.</summary>
    [JsonIgnore] public TimeSpan Duration => TimeSpan.FromMilliseconds(Steps.Sum(s => (long)s.Delay));

    [JsonIgnore] public string Summary => Loc.T("macro.summary", Steps.Count, FormatDuration(Duration));

    public static string FormatDuration(TimeSpan d) =>
        d.TotalSeconds < 60 ? $"{d.TotalSeconds:0.0} s" : $"{(int)d.TotalMinutes}:{d.Seconds:00} min";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (MacroStep s in e.OldItems) s.PropertyChanged -= OnStepChanged;
        if (e.NewItems is not null) foreach (MacroStep s in e.NewItems) s.PropertyChanged += OnStepChanged;
        RaiseSummary();
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MacroStep.Description)) return;
        if (e.PropertyName is nameof(MacroStep.Delay)) RaiseSummary();
        else Modified?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseSummary()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Duration)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-evaluates the localised texts (after a language change).</summary>
    public void RefreshTexts()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        foreach (var s in Steps) s.RefreshDescription();
    }

    public Macro Duplicate()
    {
        var copy = Json.Clone(this);
        copy.Id = Ids.New();
        copy.Created = DateTime.Now;
        return copy;
    }

    public override string ToString() => Name;
}

/// <summary>One step of a macro. <see cref="Delay"/> is the pause (ms) before the step is performed.</summary>
public partial class MacroStep : ObservableObject
{
    [ObservableProperty] private MacroStepKind _kind;

    /// <summary>Milliseconds to wait before this step (at normal speed).</summary>
    [ObservableProperty] private int _delay;

    /// <summary>Virtual key (key steps).</summary>
    [ObservableProperty] private int _vk;

    /// <summary>The key is an extended one (right Ctrl/Alt, arrows, NumPad Enter…).</summary>
    [ObservableProperty] private bool _extended;

    [ObservableProperty] private MouseButton _button = MouseButton.Left;

    /// <summary>Screen position (mouse steps).</summary>
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;

    /// <summary>Wheel delta (120 = one notch up, -120 = down).</summary>
    [ObservableProperty] private int _wheel;
    [ObservableProperty] private bool _horizontal;

    /// <summary>Text to type (Text steps).</summary>
    [ObservableProperty] private string? _text;

    [JsonIgnore] public bool IsKey => Kind is MacroStepKind.KeyDown or MacroStepKind.KeyUp or MacroStepKind.KeyPress;
    [JsonIgnore] public bool IsMouseButton => Kind is MacroStepKind.MouseDown or MacroStepKind.MouseUp or MacroStepKind.Click;
    [JsonIgnore] public bool HasPosition => IsMouseButton || Kind == MacroStepKind.MouseMove;
    [JsonIgnore] public bool IsText => Kind == MacroStepKind.Text;
    [JsonIgnore] public bool IsWheel => Kind == MacroStepKind.Wheel;

    [JsonIgnore] public string KeyName
    {
        get => Vk == 0 ? "?" : KeyNames.FromVk(Vk, Extended);
        set
        {
            var vk = KeyNames.ToVk(KeyNames.Canonical(value ?? ""));
            if (vk == 0) return;
            Extended = KeyNames.Canonical(value ?? "") == "NumPadEnter" || KeyNames.IsExtendedVk(vk);
            Vk = vk;
        }
    }

    [JsonIgnore]
    public string Description => Kind switch
    {
        MacroStepKind.KeyDown => Loc.T("macro.step.KeyDown", KeyName),
        MacroStepKind.KeyUp => Loc.T("macro.step.KeyUp", KeyName),
        MacroStepKind.KeyPress => Loc.T("macro.step.KeyPress", KeyName),
        MacroStepKind.Text => Loc.T("macro.step.Text", Text ?? ""),
        MacroStepKind.MouseDown => Loc.T("macro.step.MouseDown", ButtonName(Button), X, Y),
        MacroStepKind.MouseUp => Loc.T("macro.step.MouseUp", ButtonName(Button), X, Y),
        MacroStepKind.Click => Loc.T("macro.step.Click", ButtonName(Button), X, Y),
        MacroStepKind.MouseMove => Loc.T("macro.step.MouseMove", X, Y),
        MacroStepKind.Wheel => Loc.T(Horizontal ? "macro.step.HWheel" : "macro.step.Wheel", Wheel > 0 ? "↑" : "↓", Math.Max(1, Math.Abs(Wheel) / 120)),
        _ => Kind.ToString(),
    };

    public static string ButtonName(MouseButton b) => Loc.T($"macro.button.{b}");

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Description) or nameof(Delay)))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Description)));
            if (e.PropertyName is nameof(Vk) or nameof(Extended)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(KeyName)));
            if (e.PropertyName is nameof(Kind))
            {
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsKey)));
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsMouseButton)));
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasPosition)));
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsText)));
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsWheel)));
            }
        }
    }

    public void RefreshDescription() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Description)));

    // Not MemberwiseClone: it would also copy the PropertyChanged subscribers.
    public MacroStep Clone() => new()
    {
        Kind = Kind, Delay = Delay, Vk = Vk, Extended = Extended, Button = Button,
        X = X, Y = Y, Wheel = Wheel, Horizontal = Horizontal, Text = Text,
    };
}

/// <summary>Editing helpers that clean up a recording.</summary>
public static class MacroTools
{
    /// <summary>Joins "down + up" of the same key/button into a single press/click.</summary>
    public static List<MacroStep> MergePresses(IReadOnlyList<MacroStep> steps)
    {
        var result = new List<MacroStep>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var next = i + 1 < steps.Count ? steps[i + 1] : null;
            if (next is not null && s.Kind == MacroStepKind.KeyDown && next.Kind == MacroStepKind.KeyUp && next.Vk == s.Vk)
            {
                var press = s.Clone();
                press.Kind = MacroStepKind.KeyPress;
                result.Add(press);
                i++;
                continue;
            }
            if (next is not null && s.Kind == MacroStepKind.MouseDown && next.Kind == MacroStepKind.MouseUp &&
                next.Button == s.Button && Math.Abs(next.X - s.X) <= 3 && Math.Abs(next.Y - s.Y) <= 3)
            {
                var click = s.Clone();
                click.Kind = MacroStepKind.Click;
                result.Add(click);
                i++;
                continue;
            }
            result.Add(s);
        }
        return result;
    }

    /// <summary>Drops the mouse movements; their time is added to the following step so the timing is kept.</summary>
    public static List<MacroStep> RemoveMoves(IReadOnlyList<MacroStep> steps)
    {
        var result = new List<MacroStep>(steps.Count);
        var carried = 0;
        foreach (var s in steps)
        {
            if (s.Kind == MacroStepKind.MouseMove)
            {
                carried += s.Delay;
                continue;
            }
            var copy = s.Clone();
            copy.Delay += carried;
            carried = 0;
            result.Add(copy);
        }
        return result;
    }

    /// <summary>Multiplies every delay by <paramref name="factor"/>.</summary>
    public static void ScaleDelays(IEnumerable<MacroStep> steps, double factor)
    {
        foreach (var s in steps) s.Delay = (int)Math.Round(Math.Clamp(s.Delay * factor, 0, 3_600_000));
    }

    /// <summary>Limits every delay to <paramref name="max"/> ms.</summary>
    public static void CapDelays(IEnumerable<MacroStep> steps, int max)
    {
        foreach (var s in steps) if (s.Delay > max) s.Delay = max;
    }
}
