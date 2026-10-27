using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Model;

namespace KDesktop.Views;

/// <summary>
/// A time of day («HH:mm») as hours and minutes: digits only, the arrows and the wheel step them (the minutes five at a
/// time with the wheel), two digits of hours jump to the minutes. <see cref="Optional"/>: it may be left empty (a cross
/// empties it). Saves «HH:mm», or an empty text.
/// </summary>
public partial class TimeField : UserControl
{
    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(string), typeof(TimeField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((TimeField)d).Show()));

    public static readonly DependencyProperty OptionalProperty = DependencyProperty.Register(
        nameof(Optional), typeof(bool), typeof(TimeField), new PropertyMetadata(false, (d, _) => ((TimeField)d).Show()));

    /// <summary>Showing <see cref="Time"/>: the boxes change, but it is not the user typing.</summary>
    private bool _showing;

    /// <summary>Saving what is typed: the boxes stay as they are (half typed).</summary>
    private bool _committing;

    public TimeField()
    {
        InitializeComponent();
        foreach (var box in new[] { Hours, Minutes })
        {
            box.PreviewTextInput += OnTextInput;
            box.PreviewKeyDown += OnKey;
            box.PreviewMouseWheel += OnWheel;
            box.TextChanged += OnTextChanged;
            box.TextChanged += (_, _) => (box == Hours ? HoursHint : MinutesHint).Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
            // A click puts the caret, then selects the two digits (typing replaces them).
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

    /// <summary>«HH:mm», or empty.</summary>
    public string Time
    {
        get => (string)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    /// <summary>Whether it may be empty (a range of hours that is not used, say).</summary>
    public bool Optional
    {
        get => (bool)GetValue(OptionalProperty);
        set => SetValue(OptionalProperty, value);
    }

    private void Show()
    {
        var time = EventTime.Parse(Time);
        if (!_committing)
        {
            _showing = true;
            try
            {
                Hours.Text = time?.Hour.ToString("00", CultureInfo.InvariantCulture) ?? (Optional ? string.Empty : Hours.Text);
                Minutes.Text = time?.Minute.ToString("00", CultureInfo.InvariantCulture) ?? (Optional ? string.Empty : Minutes.Text);
            }
            finally { _showing = false; }
        }
        ClearButton.Visibility = Optional && time is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int? Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>What the boxes say, saved when it is a time (the minutes left empty are «00»); both empty, nothing when optional.</summary>
    private void Commit()
    {
        _committing = true;
        try { Save(); }
        finally { _committing = false; }
    }

    private void Save()
    {
        var hours = Number(Hours.Text);
        var minutes = Minutes.Text.Length == 0 ? 0 : Number(Minutes.Text);
        if (Hours.Text.Length == 0 && Minutes.Text.Length == 0)
        {
            if (Optional) Time = string.Empty;
            return;
        }
        if (hours is >= 0 and <= 23 && minutes is >= 0 and <= 59)
            Time = $"{hours:00}:{minutes:00}";
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_showing) return;
        Commit();
        // Two digits of hours: on to the minutes.
        if (sender == Hours && Hours.Text.Length == 2 && Hours.CaretIndex == 2 && Hours.IsKeyboardFocused) Minutes.Focus();
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text is ":" or "." or "," or "h" && sender == Hours)
        {
            Minutes.Focus();
            e.Handled = true;
            return;
        }
        if (!e.Text.All(char.IsAsciiDigit)) e.Handled = true;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        // «21:30» pasted anywhere fills both.
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text && EventTime.Parse(text) is { } time)
        {
            Time = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            Show();
        }
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
            case Key.Right when box == Hours && box.CaretIndex == box.Text.Length && box.SelectionLength == 0:
                Minutes.Focus();
                e.Handled = true;
                break;
            case Key.Left or Key.Back when box == Minutes && box.CaretIndex == 0 && box.SelectionLength == 0:
                Hours.Focus();
                Hours.CaretIndex = Hours.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var box = (TextBox)sender;
        if (!box.IsKeyboardFocusWithin) box.Focus();
        Step(box, (e.Delta > 0 ? 1 : -1) * (box == Minutes ? 5 : 1));
        e.Handled = true;
    }

    /// <summary>Up or down by <paramref name="by"/>, going round (23 → 0); an empty time starts at 00:00.</summary>
    private void Step(TextBox box, int by)
    {
        var hours = Number(Hours.Text) ?? 0;
        var minutes = Number(Minutes.Text) ?? 0;
        if (box == Hours) hours = ((hours + by) % 24 + 24) % 24;
        else
        {
            // Five at a time lands on the fives (07 → 10, 07 → 05).
            var step = Math.Abs(by);
            var snapped = by > 0 ? (minutes / step + 1) * step : (minutes + step - 1) / step * step - step;
            minutes = (snapped % 60 + 60) % 60;
        }
        Time = $"{hours:00}:{minutes:00}";
        _showing = true;
        try
        {
            Hours.Text = hours.ToString("00", CultureInfo.InvariantCulture);
            Minutes.Text = minutes.ToString("00", CultureInfo.InvariantCulture);
        }
        finally { _showing = false; }
        box.SelectAll();
        ClearButton.Visibility = Optional ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Time = string.Empty;
        _showing = true;
        try
        {
            Hours.Text = string.Empty;
            Minutes.Text = string.Empty;
        }
        finally { _showing = false; }
        ClearButton.Visibility = Visibility.Collapsed;
    }
}
