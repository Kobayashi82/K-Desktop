using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Views;

/// <summary>Seven toggle buttons (L M X J V S D) bound to a <see cref="WeekDays"/> value; none pressed = every day.</summary>
public sealed class WeekDaysPicker : WrapPanel
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(WeekDays), typeof(WeekDaysPicker),
        new FrameworkPropertyMetadata(WeekDays.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WeekDaysPicker)d).Sync()));

    public WeekDays Days
    {
        get => (WeekDays)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    private static readonly DayOfWeek[] Order =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly List<ToggleButton> _buttons = [];
    private bool _syncing;

    public WeekDaysPicker()
    {
        foreach (var day in Order)
        {
            var button = new ToggleButton
            {
                Content = Loc.T($"event.day.{day}"),
                MinWidth = 44,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 5, 8, 5),
                Tag = WeekDaysExtensions.Of(day),
            };
            button.Checked += OnToggled;
            button.Unchecked += OnToggled;
            _buttons.Add(button);
            Children.Add(button);
        }
        Sync();
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var days = WeekDays.None;
        foreach (var b in _buttons)
            if (b.IsChecked == true) days |= (WeekDays)b.Tag;
        Days = days;
    }

    private void Sync()
    {
        _syncing = true;
        foreach (var b in _buttons) b.IsChecked = Days != WeekDays.None && (Days & (WeekDays)b.Tag) != 0;
        _syncing = false;
    }
}
