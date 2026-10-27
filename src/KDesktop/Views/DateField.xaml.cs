using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KDesktop.Core.Localization;

namespace KDesktop.Views;

/// <summary>
/// A day as day / month / year in the order of the language (in Spanish «31 / 12 / 2026»): digits only, the arrows and
/// the wheel step each part, a full part jumps to the next, and the button opens a calendar. It may be left empty (a
/// cross empties it). Like <see cref="TimeField"/>.
/// </summary>
public partial class DateField : UserControl
{
    public static readonly DependencyProperty DateProperty = DependencyProperty.Register(
        nameof(Date), typeof(DateTime?), typeof(DateField),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DateField)d).Show()));

    private enum Part { Day, Month, Year }

    private readonly List<(Part Part, TextBox Box, TextBlock Hint)> _parts = [];
    private bool _showing;
    private bool _committing;

    public DateField()
    {
        InitializeComponent();
        var pattern = Loc.Instance.Culture.DateTimeFormat.ShortDatePattern;
        var order = new[] { (Part.Day, pattern.IndexOf('d', StringComparison.Ordinal)), (Part.Month, pattern.IndexOf('M', StringComparison.Ordinal)), (Part.Year, pattern.IndexOf('y', StringComparison.Ordinal)) }
            .OrderBy(p => p.Item2 < 0 ? int.MaxValue : p.Item2).Select(p => p.Item1).ToList();
        foreach (var part in order)
        {
            if (Parts.Children.Count > 0)
                Parts.Children.Add(new TextBlock { Text = "/", FontSize = 14, Margin = new Thickness(2, 0, 2, 1), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") });
            var width = part == Part.Year ? 42 : 26;
            var box = new TextBox { Style = (Style)FindResource("Part"), Width = width, MaxLength = part == Part.Year ? 4 : 2 };
            System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(part switch { Part.Day => "time.day", Part.Month => "time.month", _ => "time.year" }));
            var hint = new TextBlock
            {
                Text = part == Part.Year ? "----" : "--", FontSize = 14, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
            };
            var cell = new Grid();
            cell.Children.Add(hint);
            cell.Children.Add(box);
            Parts.Children.Add(cell);
            _parts.Add((part, box, hint));
            box.PreviewTextInput += OnTextInput;
            box.PreviewKeyDown += OnKey;
            box.PreviewMouseWheel += OnWheel;
            box.TextChanged += OnTextChanged;
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
            box.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (box.IsKeyboardFocusWithin) return;
                box.Focus();
                e.Handled = true;
            };
            DataObject.AddPastingHandler(box, OnPaste);
        }
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            Frame.SetResourceReference(Border.BorderBrushProperty, IsKeyboardFocusWithin ? "Kd.Accent" : "ControlElevationBorderBrush");
            if (!IsKeyboardFocusWithin) Show();
        };
        Show();
    }

    /// <summary>The day (only its date counts), or null.</summary>
    public DateTime? Date
    {
        get => (DateTime?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    private TextBox Box(Part part) => _parts.First(p => p.Part == part).Box;

    private void Show()
    {
        var date = Date;
        if (!_committing)
        {
            _showing = true;
            try
            {
                foreach (var (part, box, _) in _parts)
                    box.Text = date is not { } d ? string.Empty : part switch
                    {
                        Part.Day => d.Day.ToString("00", CultureInfo.InvariantCulture),
                        Part.Month => d.Month.ToString("00", CultureInfo.InvariantCulture),
                        _ => d.Year.ToString("0000", CultureInfo.InvariantCulture),
                    };
            }
            finally { _showing = false; }
        }
        foreach (var (_, box, hint) in _parts) hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        ClearButton.Visibility = date is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static int? Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>What the boxes say, saved when it is a real day; all three empty, no day.</summary>
    private void Commit()
    {
        _committing = true;
        try
        {
            if (_parts.All(p => p.Box.Text.Length == 0)) Date = null;
            else if (Number(Box(Part.Day).Text) is { } day && Number(Box(Part.Month).Text) is { } month && Number(Box(Part.Year).Text) is { } year
                     && Box(Part.Year).Text.Length == 4 && month is >= 1 and <= 12 && year >= 1 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                Date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local);
        }
        finally { _committing = false; }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        foreach (var p in _parts.Where(p => p.Box == box)) p.Hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        if (_showing) return;
        Commit();
        // A full part: on to the next one.
        if (box.Text.Length == box.MaxLength && box.CaretIndex == box.MaxLength && box.IsKeyboardFocused) Next(box)?.Focus();
    }

    private TextBox? Next(TextBox box)
    {
        var i = _parts.FindIndex(p => p.Box == box);
        return i + 1 < _parts.Count ? _parts[i + 1].Box : null;
    }

    private TextBox? Previous(TextBox box)
    {
        var i = _parts.FindIndex(p => p.Box == box);
        return i > 0 ? _parts[i - 1].Box : null;
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text is "/" or "-" or "." or " ")
        {
            Next((TextBox)sender)?.Focus();
            e.Handled = true;
            return;
        }
        if (!e.Text.All(char.IsAsciiDigit)) e.Handled = true;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        // A whole date pasted fills the three.
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text
            && DateTime.TryParse(text, Loc.Instance.Culture, DateTimeStyles.AssumeLocal, out var date))
            Date = date.Date;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        switch (e.Key)
        {
            case Key.Up or Key.Down:
                Step(box, e.Key == Key.Up ? 1 : -1);
                e.Handled = true;
                break;
            case Key.Right when box.CaretIndex == box.Text.Length && box.SelectionLength == 0 && Next(box) is { } next:
                next.Focus();
                e.Handled = true;
                break;
            case Key.Left or Key.Back when box.CaretIndex == 0 && box.SelectionLength == 0 && Previous(box) is { } previous:
                previous.Focus();
                previous.CaretIndex = previous.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var box = (TextBox)sender;
        if (!box.IsKeyboardFocusWithin) box.Focus();
        Step(box, e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// <summary>A day, month or year more or less (an empty date starts today).</summary>
    private void Step(TextBox box, int by)
    {
        var date = Date ?? DateTime.Today;
        var part = _parts.First(p => p.Box == box).Part;
        Date = part switch
        {
            Part.Day => date.AddDays(by),
            Part.Month => date.AddMonths(by),
            _ => date.AddYears(by),
        };
        box.SelectAll();
    }

    private void OnClear(object sender, RoutedEventArgs e) => Date = null;

    private void OnCalendar(object sender, RoutedEventArgs e)
    {
        Picker.SelectedDate = Date;
        Picker.DisplayDate = Date ?? DateTime.Today;
        CalendarPopup.IsOpen = true;
        Picker.Focus();
    }

    private void OnPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (!CalendarPopup.IsOpen || Picker.SelectedDate is not { } picked) return;
        Date = picked;
        CalendarPopup.IsOpen = false;
        // The calendar keeps the mouse captured after a click: let it go, or the next click is lost.
        Mouse.Capture(null);
    }
}
