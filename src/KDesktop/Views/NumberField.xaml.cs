using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace KDesktop.Views;

/// <summary>
/// A whole number with − and + (they repeat while held), arrows and Page Up/Down, and the wheel while it has the focus.
/// Only digits can be typed; what is typed is saved on Enter or on leaving it, and something out of range shows in red
/// and goes back to the last good value.
/// </summary>
public partial class NumberField : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int), typeof(NumberField),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumberField)d).ShowValue()));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumberField),
        new PropertyMetadata(0, (d, _) => ((NumberField)d).ShowValue()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumberField),
        new PropertyMetadata(int.MaxValue, (d, _) => ((NumberField)d).ShowValue()));

    public NumberField()
    {
        InitializeComponent();
        InputMethod.SetIsInputMethodEnabled(Box, false);
        Box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(Box, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || text.Trim().Length == 0 || !text.Trim().All(char.IsAsciiDigit))
                e.CancelCommand();
        });
        Box.PreviewKeyDown += OnKey;
        Box.TextChanged += (_, _) => Box.SetResourceReference(ForegroundProperty, Parse() is null ? "Kd.Danger" : "TextFillColorPrimaryBrush");
        // Keyboard.ClearFocus (InputPolish) raises LostFocus but not the keyboard one: both save.
        Box.LostKeyboardFocus += (_, _) => Commit();
        Box.LostFocus += (_, _) => Commit();
        PreviewMouseWheel += (_, e) =>
        {
            if (!Box.IsKeyboardFocusWithin) return;
            Step(e.Delta > 0 ? Increment : -Increment);
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            var name = AutomationProperties.GetName(this);
            AutomationProperties.SetName(Box, name);
            AutomationProperties.SetName(DownButton, "− " + name);
            AutomationProperties.SetName(UpButton, "+ " + name);
        };
        ShowValue();
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>What − and + add (Page Up/Down add ten times as much).</summary>
    public int Increment { get; set; } = 1;

    private void OnKey(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Up => Increment,
            Key.Down => -Increment,
            Key.PageUp => Increment * 10,
            Key.PageDown => -Increment * 10,
            _ => 0,
        };
        if (step != 0) Step(step);
        else if (e.Key == Key.Enter) Commit();
        else if (e.Key == Key.Space) { } // would get through PreviewTextInput
        else return;
        e.Handled = true;
    }

    private void OnDown(object sender, RoutedEventArgs e) => Step(-Increment);

    private void OnUp(object sender, RoutedEventArgs e) => Step(Increment);

    private void Step(int by)
    {
        var from = Parse() ?? Value;
        Set((int)Math.Clamp((long)from + by, Minimum, Maximum));
    }

    /// <summary>What is typed, or null when it is not a number in range.</summary>
    private int? Parse() =>
        int.TryParse(Box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= Minimum && n <= Maximum ? n : null;

    private void Commit()
    {
        if (Parse() is { } n) Set(n);
        else ShowValue();
    }

    private void Set(int value)
    {
        if (value != Value) Value = value;
        ShowValue();
        GetBindingExpression(ValueProperty)?.UpdateSource();
    }

    private void ShowValue()
    {
        var text = Value.ToString(CultureInfo.InvariantCulture);
        if (Box.Text != text) Box.Text = text;
        Box.CaretIndex = text.Length;
        DownButton.IsEnabled = Value > Minimum;
        UpButton.IsEnabled = Value < Maximum;
    }
}
