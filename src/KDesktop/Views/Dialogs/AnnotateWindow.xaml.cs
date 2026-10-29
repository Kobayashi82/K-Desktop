using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KDesktop.Core.Engine;
using KDesktop.Core.Imaging;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Services;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// «Anotar la captura» (F1): after a screenshot, arrows, boxes, text, highlighter, blur and crop over it, and then copy
/// or save it. Each mark can be undone and redone; Esc (or closing it) cancels and the command does not go on.
/// </summary>
public partial class AnnotateWindow : FluentWindow
{
    /// <summary>The colours of the icons (their darker tone), the ones that read on a screenshot.</summary>
    private static readonly string[] Colors = ["red", "orange", "yellow", "green", "blue", "purple", "dark"];

    /// <summary>Line width, text size and marker width of «Fino», «Medio» and «Grueso» (picture pixels).</summary>
    private static readonly (double Line, double Text, double Marker)[] SizeSteps = [(3, 18, 14), (5, 26, 22), (9, 38, 32)];

    private readonly TaskCompletionSource<AnnotatedCapture?> _done = new();
    private readonly Stack<AnnotateMark> _redo = new();
    private readonly Core.Model.AppSettings _settings = App.Current.Config.Settings;
    private readonly ScreenRect _area;
    private AnnotateTool _tool = AnnotateTool.Arrow;
    private Point? _start;
    private List<Point>? _stroke;
    private TextBox? _typing;
    private Point _typingAt;
    private bool _finished;

    private AnnotateWindow(BgraImage image, ScreenRect area, bool copy, bool save)
    {
        InitializeComponent();
        _area = area;
        Surface.Load(image);
        BuildTools();
        BuildSwatches();
        BuildSizes();
        // The button of what the action does stands out (copy only: «Copiar»).
        if (copy && !save)
        {
            CopyButton.Appearance = ControlAppearance.Primary;
            SaveButton.Appearance = ControlAppearance.Secondary;
        }
        Surface.MouseLeftButtonDown += OnDown;
        Surface.MouseMove += OnMove;
        Surface.MouseLeftButtonUp += OnUp;
        Surface.SizeChanged += (_, _) => Commit();
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => Place();
        Closed += (_, _) => _done.TrySetResult(null);
        Refresh();
    }

    /// <summary>Opens the editor (UI thread) and waits for what is chosen in it; null when cancelled.</summary>
    public static Task<AnnotatedCapture?> EditAsync(BgraImage image, ScreenRect area, bool copy, bool save, bool demo = false)
    {
        var window = new AnnotateWindow(image, area, copy, save);
        if (demo) window.AddDemoMarks();
        window.Show();
        window.Activate();
        WindowTools.ForceForeground(UiNative.Handle(window));
        window.Surface.Focus();
        return window._done.Task;
    }

    /// <summary>On the monitor of the capture, as big as the picture (with its bars) and at most 85 % of it, centred.</summary>
    private void Place()
    {
        var hwnd = UiNative.Handle(this);
        var center = new UiNative.POINT { X = _area.Left + _area.Width / 2, Y = _area.Top + _area.Height / 2 };
        var work = UiNative.WorkAreaAt(center);
        var dpi = UiNative.GetDpiForWindow(hwnd) / 96.0;
        if (dpi <= 0) dpi = 1;
        var image = Surface.Source!;
        var width = (int)Math.Clamp(image.Width + 80 * dpi, MinWidth * dpi, work.Width * 0.85);
        var height = (int)Math.Clamp(image.Height + 230 * dpi, MinHeight * dpi, work.Height * 0.85);
        UiNative.SetWindowPos(hwnd, IntPtr.Zero, work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height,
            UiNative.SWP_NOZORDER | UiNative.SWP_NOACTIVATE);
    }

    // ── the bars ───────────────────────────────────────────────────────────

    private void BuildTools()
    {
        (AnnotateTool Tool, SymbolRegular Icon)[] tools =
        [
            (AnnotateTool.Arrow, SymbolRegular.ArrowUpRight24), (AnnotateTool.Box, SymbolRegular.RectangleLandscape24),
            (AnnotateTool.Text, SymbolRegular.TextT24), (AnnotateTool.Highlighter, SymbolRegular.Highlight24),
            (AnnotateTool.Blur, SymbolRegular.Blur24), (AnnotateTool.Crop, SymbolRegular.Crop24),
        ];
        for (var i = 0; i < tools.Length; i++)
        {
            var (tool, icon) = tools[i];
            var name = Loc.T("annotate.tool." + tool);
            var button = new RadioButton
            {
                Style = (Style)FindResource("Segment"), GroupName = "tool", Tag = tool, IsChecked = tool == _tool,
                Content = Follows(new SymbolIcon { Symbol = icon, FontSize = 18 }, SymbolIcon.ForegroundProperty),
                ToolTip = name + " (" + (i + 1).ToString(CultureInfo.InvariantCulture) + ")",
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Checked += (_, _) => Choose(tool);
            Tools.Children.Add(button);
        }
    }

    private void BuildSwatches()
    {
        foreach (var key in Colors)
        {
            var (top, bottom) = KdIcons.Gradient(key);
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"), GroupName = "color", Tag = key,
                IsChecked = key.Equals(_settings.AnnotateColor, StringComparison.OrdinalIgnoreCase),
                ToolTip = Loc.T($"icons.color.{key}"),
                Background = new LinearGradientBrush(top, bottom, 90),
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, Loc.T($"icons.color.{key}"));
            swatch.Checked += (_, _) => _settings.AnnotateColor = key;
            Swatches.Children.Add(swatch);
        }
        if (Swatches.Children.OfType<RadioButton>().All(s => s.IsChecked != true)) ((RadioButton)Swatches.Children[0]).IsChecked = true;
    }

    private void BuildSizes()
    {
        for (var i = 0; i < SizeSteps.Length; i++)
        {
            var step = i;
            var name = Loc.T("annotate.size." + step.ToString(CultureInfo.InvariantCulture));
            var button = new RadioButton
            {
                Style = (Style)FindResource("Segment"), GroupName = "size", IsChecked = step == Math.Clamp(_settings.AnnotateSize, 0, 2),
                Content = Follows(new Rectangle { Width = 18, Height = 2 + step * 2.5, RadiusX = 1.5, RadiusY = 1.5 }, Shape.FillProperty),
                ToolTip = name,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Checked += (_, _) => _settings.AnnotateSize = step;
            Sizes.Children.Add(button);
        }
    }

    /// <summary>
    /// The content of a segment painted with the segment's own colour (grey, gold when chosen): a RadioButton's content
    /// does not take it by itself.
    /// </summary>
    private static T Follows<T>(T content, DependencyProperty property) where T : FrameworkElement
    {
        content.SetBinding(property, new System.Windows.Data.Binding(nameof(Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(RadioButton), 1),
        });
        return content;
    }

    /// <summary><c>--annotate-test … demo</c>: one mark of each kind (and a crop), to see them and the picture they make.</summary>
    internal void AddDemoMarks()
    {
        var image = Surface.Source!;
        double w = image.Width, h = image.Height;
        var red = KdIcons.Gradient("red").Bottom;
        Surface.Marks.Add(new ArrowMark(new Point(w * 0.6, h * 0.85), new Point(w * 0.35, h * 0.62), red, 5));
        Surface.Marks.Add(new BoxMark(new Rect(w * 0.05, h * 0.1, w * 0.4, h * 0.12), KdIcons.Gradient("blue").Bottom, 5));
        Surface.Marks.Add(new HighlightMark([new Point(w * 0.08, h * 0.73), new Point(w * 0.4, h * 0.73)], KdIcons.Gradient("yellow").Bottom, 22));
        Surface.Marks.Add(new TextMark(new Point(w * 0.62, h * 0.08), "¡Mira aquí!", KdIcons.Gradient("green").Bottom, 26));
        Surface.Marks.Add(Blurred(Pixels(new Rect(w * 0.18, h * 0.28, w * 0.3, h * 0.1))));
        Surface.Marks.Add(new CropMark(Pixels(new Rect(0, 0, w * 0.95, h * 0.95))));
        Refresh();
    }

    private Color CurrentColor => KdIcons.Gradient(_settings.AnnotateColor ?? Colors[0]).Bottom;

    private (double Line, double Text, double Marker) CurrentSize => SizeSteps[Math.Clamp(_settings.AnnotateSize, 0, SizeSteps.Length - 1)];

    private void Choose(AnnotateTool tool)
    {
        Commit();
        _tool = tool;
        Surface.ShowWhole = tool == AnnotateTool.Crop;
        Surface.Cursor = tool == AnnotateTool.Text ? Cursors.IBeam : Cursors.Cross;
        Refresh();
    }

    private void Refresh()
    {
        var crop = Surface.Crop;
        SizeText.Text = string.Create(CultureInfo.InvariantCulture, $"{crop.Width} × {crop.Height}");
        HintText.Text = Loc.T("annotate.hint." + _tool);
        UndoButton.IsEnabled = Surface.Marks.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        Surface.Cursor = _tool == AnnotateTool.Text ? Cursors.IBeam : Cursors.Cross;
        Surface.InvalidateVisual();
    }

    // ── drawing ────────────────────────────────────────────────────────────

    /// <summary>Inside the picture (a mark never starts or ends outside it).</summary>
    private Point Picture(MouseEventArgs e)
    {
        var p = Surface.ToPicture(e.GetPosition(Surface));
        var image = Surface.Source!;
        return new Point(Math.Clamp(p.X, 0, image.Width), Math.Clamp(p.Y, 0, image.Height));
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        Surface.Focus();
        if (_typing is not null)
        {
            Commit();
            return;
        }
        var p = Picture(e);
        if (_tool == AnnotateTool.Text)
        {
            StartText(p);
            e.Handled = true;
            return;
        }
        _start = p;
        _stroke = _tool == AnnotateTool.Highlighter ? [p] : null;
        Surface.CaptureMouse();
        OnMove(sender, e);
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_start is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = Picture(e);
        var size = CurrentSize;
        var square = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        Surface.Drawing = _tool switch
        {
            AnnotateTool.Arrow => new ArrowMark(start, square ? Straight(start, p) : p, CurrentColor, size.Line),
            AnnotateTool.Box => new BoxMark(Box(start, p, square), CurrentColor, size.Line),
            AnnotateTool.Highlighter => Stroke(p, size.Marker),
            AnnotateTool.Blur => new BlurPreview(Box(start, p, false)),
            AnnotateTool.Crop => new CropMark(Pixels(Box(start, p, false))),
            _ => null,
        };
        Surface.InvalidateVisual();
    }

    private HighlightMark Stroke(Point p, double width)
    {
        if (_stroke!.Count == 0 || (p - _stroke[^1]).Length >= 2) _stroke.Add(p);
        return new HighlightMark([.. _stroke], CurrentColor, width);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is null) return;
        Surface.ReleaseMouseCapture();
        _start = null;
        var drawn = Surface.Drawing;
        Surface.Drawing = null;
        AnnotateMark? mark = drawn switch
        {
            ArrowMark a when (a.To - a.From).Length >= 4 => a,
            BoxMark b when b.Area.Width >= 3 && b.Area.Height >= 3 => b,
            HighlightMark h => h,
            BlurPreview p when p.Area.Width >= 3 && p.Area.Height >= 3 => Blurred(Pixels(p.Area)),
            CropMark c when c.Area.Width >= 8 && c.Area.Height >= 8 => c,
            _ => null,
        };
        if (mark is not null) Add(mark);
        // After cropping, back to drawing on what is kept.
        if (mark is CropMark) ((RadioButton)Tools.Children[0]).IsChecked = true;
        Refresh();
    }

    private BlurMark Blurred(Int32Rect area)
    {
        var source = Surface.Source!;
        var piece = source.Crop(area.X, area.Y, area.Width, area.Height);
        // Blocks of ~1/60 of the picture's shorter side (8–24 px): small text and faces can't be made out.
        var block = (int)Math.Clamp(Math.Min(source.Width, source.Height) / 60.0, 8, 24);
        ImageEffects.Blur(piece, 0, 0, piece.Width, piece.Height, block);
        var patch = BitmapSource.Create(piece.Width, piece.Height, 96, 96, PixelFormats.Bgra32, null, piece.Pixels, piece.Stride);
        patch.Freeze();
        return new BlurMark(area, patch);
    }

    private void Add(AnnotateMark mark)
    {
        Surface.Marks.Add(mark);
        _redo.Clear();
    }

    private static Point Straight(Point from, Point to)
    {
        // Shift: in steps of 45°.
        var v = to - from;
        var angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return from + new Vector(Math.Cos(angle), Math.Sin(angle)) * v.Length;
    }

    private static Rect Box(Point a, Point b, bool square)
    {
        if (square)
        {
            var side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            b = new Point(a.X + Math.Sign(b.X - a.X) * side, a.Y + Math.Sign(b.Y - a.Y) * side);
        }
        return new Rect(a, b);
    }

    private Int32Rect Pixels(Rect r)
    {
        var image = Surface.Source!;
        var x = (int)Math.Clamp(Math.Floor(r.X), 0, image.Width);
        var y = (int)Math.Clamp(Math.Floor(r.Y), 0, image.Height);
        var right = (int)Math.Clamp(Math.Ceiling(r.Right), 0, image.Width);
        var bottom = (int)Math.Clamp(Math.Ceiling(r.Bottom), 0, image.Height);
        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    // ── text ───────────────────────────────────────────────────────────────

    private void StartText(Point at)
    {
        var (scale, _) = Surface.Fit();
        _typingAt = at;
        var view = Surface.ToView(at);
        _typing = new TextBox
        {
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, FontSize = Math.Max(8, CurrentSize.Text * scale),
            Foreground = new SolidColorBrush(CurrentColor), Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            CaretBrush = new SolidColorBrush(CurrentColor), BorderThickness = new Thickness(1), Padding = new Thickness(0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), MinWidth = 40, AcceptsReturn = true,
        };
        _typing.KeyDown += OnTypingKey;
        // The box adds its border and padding: the text lands where it was typed.
        Canvas.SetLeft(_typing, view.X - 1);
        Canvas.SetTop(_typing, view.Y - 1);
        TextLayer.Children.Add(_typing);
        _typing.Focus();
    }

    private void OnTypingKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Commit(keep: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            Commit();
            e.Handled = true;
        }
    }

    /// <summary>The text being written becomes a mark (or is dropped).</summary>
    private void Commit(bool keep = true)
    {
        if (_typing is not { } box) return;
        _typing = null;
        TextLayer.Children.Remove(box);
        var text = box.Text.TrimEnd();
        if (keep && text.Trim().Length > 0)
        {
            var color = ((SolidColorBrush)box.Foreground).Color;
            var (scale, _) = Surface.Fit();
            Add(new TextMark(_typingAt, text, color, box.FontSize / scale));
        }
        Surface.Focus();
        Refresh();
    }

    // ── undo, keys, the end ────────────────────────────────────────────────

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        Commit();
        if (Surface.Marks.Count == 0) return;
        _redo.Push(Surface.Marks[^1]);
        Surface.Marks.RemoveAt(Surface.Marks.Count - 1);
        Refresh();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        Surface.Marks.Add(_redo.Pop());
        Refresh();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_typing is not null) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.Escape: OnCancel(this, e); break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl: OnRedo(this, e); break;
            case Key.Z when ctrl: OnUndo(this, e); break;
            case Key.C when ctrl: OnCopy(this, e); break;
            case Key.S when ctrl: OnSave(this, e); break;
            case >= Key.D1 and <= Key.D6 when !ctrl:
                ((RadioButton)Tools.Children[e.Key - Key.D1]).IsChecked = true;
                break;
            case >= Key.NumPad1 and <= Key.NumPad6 when !ctrl:
                ((RadioButton)Tools.Children[e.Key - Key.NumPad1]).IsChecked = true;
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// «Copiar texto»: what the picture says, read with the OCR of Windows as «Copiar texto de la pantalla» does, from the
    /// picture as it is now (cropped, and what is blurred stays unreadable). The editor stays open.
    /// </summary>
    private async void OnCopyText(object sender, RoutedEventArgs e)
    {
        Commit();
        var ui = App.Current.Ui;
        var look = OsdLook.Of("ScanText24");
        if (!ScreenText.Available)
        {
            ui.ShowOsd(Loc.T("osd.screenTextNoOcr"), OsdKind.Warning, look: look);
            return;
        }
        TextButton.IsEnabled = false;
        try
        {
            var read = await ScreenText.ReadAsync(Surface.Render(), Loc.Instance.Language);
            if (read is null || read.Text.Length == 0)
            {
                ui.ShowOsd(Loc.T("osd.screenTextNone"), OsdKind.Warning, look: look);
                return;
            }
            var copied = ui.SetClipboardText(read.Text);
            ui.ShowOsd(copied ? Loc.T(read.Lines == 1 ? "osd.screenTextCopiedOne" : "osd.screenTextCopied", read.Lines, CommandEngine.Preview(read.Text)) : Loc.T("osd.screenTextNotCopied"),
                copied ? OsdKind.Success : OsdKind.Warning, look: look);
        }
        finally
        {
            TextButton.IsEnabled = true;
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e) => Finish(copy: true, save: false);

    private void OnSave(object sender, RoutedEventArgs e) => Finish(copy: false, save: true);

    private void Finish(bool copy, bool save)
    {
        if (_finished) return;
        Commit();
        _finished = true;
        _done.TrySetResult(new AnnotatedCapture(Surface.Render(), copy, save));
        Close();
    }
}
