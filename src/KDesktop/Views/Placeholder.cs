using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace KDesktop.Views;

/// <summary>
/// Grey hint text inside an editable ComboBox while it is empty (WPF-UI only has it on its TextBox):
/// <c>v:Placeholder.Text="{l:Tr …}"</c>. The hint is added to the combo's template, in the cell of its text box, so
/// it moves with the combo (an adorner lags behind the pages' slide-in animations).
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Placeholder), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    private static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(Hint), typeof(Placeholder));

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox box) return;
        if (box.GetValue(HintProperty) is Hint existing)
        {
            existing.SetText(e.NewValue as string);
            return;
        }
        if (e.OldValue is not null) return; // handlers already hooked
        box.Loaded += (_, _) => Attach(box);
        // A combo in a collapsed section has no template yet when it loads: attach once it is shown.
        box.IsVisibleChanged += (_, _) =>
        {
            if (box.IsVisible && box.IsLoaded) Attach(box);
        };
        if (box.IsLoaded) Attach(box);
    }

    private static void Attach(ComboBox box)
    {
        if (box.GetValue(HintProperty) is not null) return;
        box.ApplyTemplate();
        if (box.Template?.FindName("PART_EditableTextBox", box) is not TextBox text) return;
        if (VisualTreeHelper.GetParent(text) is not Panel panel) return;
        box.SetValue(HintProperty, new Hint(text, panel, GetText(box)));
    }

    /// <summary>The hint text block, kept on the caret's line while the box is empty.</summary>
    private sealed class Hint
    {
        private readonly TextBox _box;
        private readonly TextBlock _block = new()
        {
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top,
        };

        public Hint(TextBox box, Panel panel, string? text)
        {
            _box = box;
            _block.SetResourceReference(TextBlock.ForegroundProperty, "TextPlaceholderColorBrush");
            _block.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(Control.FontSize)) { Source = box });
            _block.SetBinding(TextBlock.FontFamilyProperty, new Binding(nameof(Control.FontFamily)) { Source = box });
            foreach (var dp in new[] { Grid.RowProperty, Grid.ColumnProperty, Grid.RowSpanProperty, Grid.ColumnSpanProperty })
                _block.SetValue(dp, box.GetValue(dp));
            panel.Children.Add(_block);
            box.TextChanged += (_, _) => Update();
            box.LayoutUpdated += (_, _) => Place();
            SetText(text);
        }

        public void SetText(string? text)
        {
            _block.Text = text ?? string.Empty;
            Update();
        }

        private void Update() =>
            _block.Visibility = string.IsNullOrEmpty(_box.Text) && _block.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Exactly where the caret is (an empty box still reports it), relative to the box, which fills the same
        /// cell. Only changed when it moved, so the layout settles.
        /// </summary>
        private void Place()
        {
            if (_block.Visibility != Visibility.Visible) return;
            var caret = _box.GetRectFromCharacterIndex(0);
            if (caret.IsEmpty || double.IsInfinity(caret.X) || double.IsNaN(caret.X)) return;
            var top = caret.Top + (caret.Height - _block.ActualHeight) / 2;
            var margin = new Thickness(Math.Round(_box.Margin.Left + caret.X), Math.Round(_box.Margin.Top + Math.Max(0, top)), 4, 0);
            if (_block.Margin != margin) _block.Margin = margin;
        }
    }
}
