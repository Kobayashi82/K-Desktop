using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Views;

/// <summary>
/// <c>v:VariableHints.IsEnabled="True"</c> on a text box of an action (5.0.338): typing «{» shows the variables it can use
/// ({fecha}, {resultado}, {seleccion}… and the ones the command's actions save with a name), filtered while typing;
/// arrows to choose, Enter or Tab to put it, Esc to close.
/// </summary>
public static class VariableHints
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(VariableHints), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        var hints = new Hints(box);
        box.TextChanged += (_, _) => hints.Update();
        box.PreviewKeyDown += hints.OnKey;
        box.LostKeyboardFocus += (_, _) => hints.Close();
    }

    private sealed record Choice(string Name, string Description);

    private sealed class Hints(TextBox box)
    {
        private readonly Popup _popup = new()
        {
            PlacementTarget = box,
            Placement = PlacementMode.RelativePoint,
            AllowsTransparency = true,
            StaysOpen = true,
            Focusable = false,
        };
        private readonly StackPanel _rows = new();
        private List<Choice> _shown = [];
        private int _selected;
        private int _start = -1; // where its «{» is

        public void Update()
        {
            var caret = box.CaretIndex;
            var text = box.Text;
            var open = caret;
            while (open > 0 && IsNameChar(text[open - 1])) open--;
            if (open == 0 || text[open - 1] != '{' || box.SelectionLength > 0)
            {
                Close();
                return;
            }
            var typed = text[open..caret];
            _shown = [.. Choices(box).Where(c => c.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && !c.Name.Equals(typed, StringComparison.OrdinalIgnoreCase))];
            if (_shown.Count == 0)
            {
                Close();
                return;
            }
            _start = open - 1;
            _selected = 0;
            Fill();
            var at = box.GetRectFromCharacterIndex(_start);
            _popup.HorizontalOffset = at.IsEmpty ? 0 : at.Left;
            _popup.VerticalOffset = at.IsEmpty ? box.ActualHeight : at.Bottom + 4;
            _popup.IsOpen = true;
        }

        public void Close()
        {
            _popup.IsOpen = false;
            _start = -1;
        }

        public void OnKey(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen) return;
            switch (e.Key)
            {
                case Key.Down:
                    _selected = (_selected + 1) % _shown.Count;
                    Fill();
                    break;
                case Key.Up:
                    _selected = (_selected - 1 + _shown.Count) % _shown.Count;
                    Fill();
                    break;
                case Key.Enter or Key.Tab:
                    Put(_shown[_selected]);
                    break;
                case Key.Escape:
                    Close();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void Put(Choice choice)
        {
            if (_start < 0) return;
            var caret = box.CaretIndex;
            var start = _start;
            Close();
            var inserted = "{" + choice.Name + "}";
            // What follows the name already typed (a «}» typed before) is kept out of the way.
            var end = caret < box.Text.Length && box.Text[caret] == '}' ? caret + 1 : caret;
            box.Text = box.Text[..start] + inserted + box.Text[end..];
            box.CaretIndex = start + inserted.Length;
        }

        private void Fill()
        {
            _rows.Children.Clear();
            for (var i = 0; i < _shown.Count; i++)
            {
                var choice = _shown[i];
                var row = new Border
                {
                    Padding = new Thickness(10, 5, 10, 5),
                    CornerRadius = new CornerRadius(5),
                    Cursor = Cursors.Hand,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock { Text = "{" + choice.Name + "}", FontWeight = FontWeights.SemiBold, MinWidth = 120 },
                            new TextBlock { Text = choice.Description, Margin = new Thickness(12, 0, 0, 0), Opacity = 0.75 },
                        },
                    },
                };
                row.SetResourceReference(TextElement.ForegroundProperty, "Kd.MenuText");
                if (i == _selected) row.SetResourceReference(Border.BackgroundProperty, "Kd.MenuHover");
                row.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true; // the box keeps the focus
                    Put(choice);
                };
                _rows.Children.Add(row);
            }
            if (_popup.Child is null)
            {
                var frame = new Border
                {
                    Padding = new Thickness(4),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    MaxHeight = 300,
                    Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows, Focusable = false },
                };
                frame.SetResourceReference(Border.BackgroundProperty, "Kd.MenuTintOpaque");
                frame.SetResourceReference(Border.BorderBrushProperty, "Kd.MenuBorder");
                _popup.Child = frame;
            }
        }
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The ones that always exist (named in the language of the interface: both work) and the command's own.</summary>
    private static IEnumerable<Choice> Choices(TextBox box)
    {
        var spanish = Loc.Instance.Language == "es";
        yield return new(spanish ? "resultado" : "result", Loc.T("vars.result"));
        yield return new(spanish ? "seleccion" : "selection", Loc.T("vars.selection"));
        yield return new(spanish ? "portapapeles" : "clipboard", Loc.T("vars.clipboard"));
        yield return new(spanish ? "fecha" : "date", Loc.T("vars.date"));
        yield return new(spanish ? "hora" : "time", Loc.T("vars.time"));
        if (CommandOf(box) is not { } command) yield break;
        // Only the ones saved before the action being edited (the others do not exist yet when it runs).
        var editing = box.DataContext as CommandAction;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in command.Actions)
        {
            if (ReferenceEquals(action, editing)) break;
            if (CommandVariables.NameOf(action.SaveAs) is { } name && seen.Add(name))
                yield return new(name, Loc.T("vars.saved", action.Info.Name));
        }
    }

    /// <summary>The command being edited: the first parent whose data is one.</summary>
    private static Command? CommandOf(DependencyObject element)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: Command command }) return command;
        return null;
    }
}
