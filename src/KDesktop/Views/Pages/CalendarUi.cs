using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Localization;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiButton = Wpf.Ui.Controls.Button;

namespace KDesktop.Views.Pages;

/// <summary>Small pieces the calendar's, the accounts' and the mail's options are built with (the look of the other cards).</summary>
internal static class CalendarUi
{
    /// <summary>A card with a title and, optionally, a caption under it; its content goes in the returned panel.</summary>
    public static StackPanel Card(Panel parent, string title, string? intro = null)
    {
        var content = new StackPanel();
        var border = new Border { Child = content };
        border.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        var heading = new TextBlock { Text = title };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
        content.Children.Add(heading);
        if (intro is not null) content.Children.Add(Caption(intro, new Thickness(0, 0, 0, 10)));
        parent.Children.Add(border);
        return content;
    }

    public static TextBlock Caption(string text, Thickness? margin = null)
    {
        var caption = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 2, 0, 0) };
        caption.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
        return caption;
    }

    public static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text };
        label.SetResourceReference(FrameworkElement.StyleProperty, "FieldLabel");
        return label;
    }

    /// <summary>A setting: its name and hint on the left, its control on the right.</summary>
    public static Grid Row(string name, string? hint, UIElement control, double width = 260)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width > 0 ? new GridLength(width) : GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        texts.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap });
        if (hint is not null) texts.Children.Add(Caption(hint));
        grid.Children.Add(texts);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    public static UiButton Button(string text, SymbolRegular icon, Action click, bool primary = false, bool danger = false)
    {
        var button = new UiButton
        {
            Content = text,
            Icon = new SymbolIcon { Symbol = icon },
            Margin = new Thickness(0, 0, 8, 0),
            Appearance = primary ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary,
        };
        if (danger) button.SetResourceReference(Control.ForegroundProperty, "Kd.Danger");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A button that opens a step-by-step guide (<see cref="Dialogs.GuideDialog"/>) of what is done outside K-Desktop.</summary>
    public static UiButton GuideButton(FrameworkElement from, string textKey, string guide, Thickness margin)
    {
        var button = Button(Loc.T(textKey), SymbolRegular.BookQuestionMark24, () => Dialogs.GuideDialog.Show(Window.GetWindow(from), guide));
        button.Margin = margin;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    public static UiButton IconButton(SymbolRegular icon, string tip, Action click, bool danger = false)
    {
        var button = new UiButton { Icon = new SymbolIcon { Symbol = icon }, ToolTip = tip, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        if (danger) button.SetResourceReference(Control.ForegroundProperty, "Kd.Danger");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A line inside a card: a thin rule.</summary>
    public static Border Rule() => new Border { Height = 1, Margin = new Thickness(0, 10, 0, 10), SnapsToDevicePixels = true }.With(b => b.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush"));

    public static T With<T>(this T element, Action<T> set)
    {
        set(element);
        return element;
    }

    /// <summary>A coloured dot that, clicked, offers the palette.</summary>
    public static FrameworkElement ColorDot(string color, Action<string> chosen)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Fill = AgendaPage.Brush(color), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("calendars.color") };
        dot.MouseLeftButtonUp += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var c in Core.Calendar.CalendarColors.Palette)
            {
                var item = new MenuItem { Header = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = AgendaPage.Brush(c) } };
                var value = c;
                item.Click += (_, _) => chosen(value);
                menu.Items.Add(item);
            }
            menu.PlacementTarget = dot;
            menu.IsOpen = true;
        };
        return dot;
    }

    public static TextBlock Status(string text, string brush)
    {
        var status = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return status;
    }

    public static Brush? Resource(string key) => Application.Current.TryFindResource(key) as Brush;
}
