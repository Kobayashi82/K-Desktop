using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Stage;

/// <summary>What a window of the stage shows.</summary>
internal enum StageKind { Notes, Photos, Calculator }

internal enum StageWindowState { Normal, Minimized, Maximized, Closed }

/// <summary>A window of the stage: where it goes, how it is, what it shows and how it is moving now.</summary>
internal sealed class StageWindow(long handle, StageKind kind, string title, string appId, PixelRect home)
{
    public long Handle { get; } = handle;

    public StageKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string AppId { get; } = appId;

    /// <summary>Where it starts (and goes back to on <see cref="StageScene.Reset"/>).</summary>
    public PixelRect Home { get; } = home;

    /// <summary>Where it is when it is neither minimized nor maximized.</summary>
    public PixelRect Normal { get; set; } = home;

    public StageWindowState State { get; set; } = StageWindowState.Normal;

    /// <summary>What has been typed into it (the stage's Typing).</summary>
    public string Typed { get; set; } = "";

    /// <summary>The animation it is in (minimizing, opening…), or null.</summary>
    public StageAnimation? Animation { get; set; }

    /// <summary>Where it is in the world (what Windows would say: the end of an animation, never the middle of one).</summary>
    public PixelRect Bounds(PixelRect workArea) => State == StageWindowState.Maximized ? workArea : Normal;

    public bool IsShown => State is StageWindowState.Normal or StageWindowState.Maximized;
}

/// <summary>A window going from one place and opacity to another, eased.</summary>
internal sealed record StageAnimation(double Start, double Duration, Rect From, Rect To, double FromOpacity, double ToOpacity)
{
    public double Progress(double now) => Duration <= 0 ? 1 : Math.Clamp((now - Start) / Duration, 0, 1);

    public bool IsOver(double now) => Progress(now) >= 1;

    /// <summary>Where it is drawn now, and how opaque.</summary>
    public (Rect Rect, double Opacity) At(double now)
    {
        var t = Progress(now);
        t = 1 - Math.Pow(1 - t, 3);
        Rect rect = new(From.X + (To.X - From.X) * t, From.Y + (To.Y - From.Y) * t,
            Math.Max(1, From.Width + (To.Width - From.Width) * t), Math.Max(1, From.Height + (To.Height - From.Height) * t));
        return (rect, FromOpacity + (ToOpacity - FromOpacity) * t);
    }
}

/// <summary>
/// The showcase's stage (phase 16): a desktop of its own where an assistant shows what it does without touching anything
/// of the user's. One monitor far from every real one (so nothing of the user's screen is ever read for it), a taskbar
/// with its tray and clock, and two windows of pretend programs (a third opens to be waited for). The assistants see it as
/// their whole world (<see cref="HostWorld"/>) and move its windows through it (<see cref="IWindowServices"/>); the
/// compositor draws <see cref="Backdrop"/> under them. Engine thread.
/// </summary>
internal sealed class StageScene : IWindowServices
{
    /// <summary>The size of the stage in pixels, and its scaling (assistants are drawn as on a 125 % monitor).</summary>
    public const int Width = 720, Height = 405;

    public const double Scale = 1.25;

    /// <summary>Its top-left corner: far from any real monitor.</summary>
    public const int OriginX = 30000, OriginY = 30000;

    private const int TaskbarHeight = 40;
    private const int TitleHeight = 30;
    private const int ButtonWidth = 40;

    /// <summary>The handles of its windows: never those of a real window (they are odd and above what Windows gives).</summary>
    private const long HandleBase = 0x7E57_0000_0001;

    private const double MinimizeTime = 0.25, MaximizeTime = 0.2, CloseTime = 0.18, OpenTime = 0.22, MoveTime = 0.15;

    /// <summary>Letters typed a second while the stage pretends something is typed into a window.</summary>
    private const double TypingSpeed = 14;

    private readonly HostWorld _world;
    private readonly Func<double> _clock;
    private readonly List<StageWindow> _windows = [];
    private readonly StageWindow _calculator;
    private DrawingGroup? _backdrop;
    private bool _worldChanged = true;
    private string? _typing;
    private double _typingStart;
    private long _typingWindow;
    private double _nextSystem;
    private int _minute = -1;

    public StageScene(HostWorld world, Func<double> clock, bool dark)
    {
        _world = world;
        _clock = clock;
        Dark = dark;
        Bounds = PixelRect.FromSize(OriginX, OriginY, Width, Height);
        Taskbar = new PixelRect(Bounds.Left, Bounds.Bottom - TaskbarHeight, Bounds.Right, Bounds.Bottom);
        WorkArea = new PixelRect(Bounds.Left, Bounds.Top, Bounds.Right, Taskbar.Top);
        Clock = new PixelRect(Taskbar.Right - 70, Taskbar.Top, Taskbar.Right - 6, Taskbar.Bottom);
        Tray = new PixelRect(Clock.Left - 78, Taskbar.Top, Clock.Left, Taskbar.Bottom);
        Main = new StageWindow(HandleBase, StageKind.Notes, Loc("showcase.stage.notes"), "notepad", At(250, 78, 330, 205));
        Second = new StageWindow(HandleBase + 2, StageKind.Photos, Loc("showcase.stage.photos"), "photos", At(70, 132, 260, 168));
        _calculator = new StageWindow(HandleBase + 4, StageKind.Calculator, Loc("showcase.stage.calculator"), "calculator", At(478, 56, 186, 236))
        {
            State = StageWindowState.Closed,
        };
        _windows.AddRange([Main, Second, _calculator]);
    }

    private PixelRect At(int x, int y, int width, int height) => PixelRect.FromSize(Bounds.Left + x, Bounds.Top + y, width, height);

    private static string Loc(string key) => KDesktop.Core.Localization.Loc.T(key);

    public bool Dark { get; set; }

    /// <summary>The stage's only monitor.</summary>
    public PixelRect Bounds { get; }

    public PixelRect WorkArea { get; }

    public PixelRect Taskbar { get; }

    public PixelRect Tray { get; }

    public PixelRect Clock { get; }

    /// <summary>The window most things happen to (a note pad).</summary>
    public StageWindow Main { get; }

    /// <summary>A window behind it (photos), to bring to the front.</summary>
    public StageWindow Second { get; }

    /// <summary>
    /// The cursor on the stage (screen pixels of the stage), or null when it is not over it: the user's mouse over its picture
    /// in K-Desktop, or one the stage draws itself while recording (<see cref="DrawCursor"/>).
    /// </summary>
    public Point? Cursor
    {
        get => _cursor;
        set
        {
            if (_cursor == value) return;
            _cursor = value;
            _lastInput = _clock();
            _nextSystem = 0;
            if (DrawCursor) Changed = true;
        }
    }

    private Point? _cursor;
    private double _lastInput;

    /// <summary>The stage draws the cursor itself (recording: there is no mouse over a clip).</summary>
    public bool DrawCursor { get; set; }

    /// <summary>Something it draws changed since the compositor last took <see cref="Backdrop"/>.</summary>
    public bool Changed { get; set; } = true;

    /// <summary>Some window is moving (or being typed into): the stage wants frames.</summary>
    public bool IsAnimating => _typing is not null || _windows.Any(w => w.Animation is not null);

    /// <summary>The windows of the stage, front first (also the minimized and closed ones).</summary>
    public IReadOnlyList<StageWindow> Windows => _windows;

    public StageWindow? Find(long handle) => _windows.FirstOrDefault(w => w.Handle == handle);

    /// <summary>The window in front (shown), or null.</summary>
    public StageWindow? Front => _windows.FirstOrDefault(w => w.IsShown);

    /// <summary>Everything back where it started (between the clips of a recording).</summary>
    public void Reset()
    {
        foreach (var window in _windows)
        {
            window.Normal = window.Home;
            window.State = window == _calculator ? StageWindowState.Closed : StageWindowState.Normal;
            window.Typed = "";
            window.Animation = null;
        }
        _windows.Sort((a, b) => Order(a).CompareTo(Order(b)));
        _typing = null;
        Touch();
        static int Order(StageWindow w) => w.Kind switch { StageKind.Notes => 0, StageKind.Photos => 1, _ => 2 };
    }

    /// <summary>
    /// One step of the stage: its animations, what is being typed, and the world the assistants see (published when it
    /// changes). Returns how long it may sleep.
    /// </summary>
    public double Update(double now)
    {
        foreach (var window in _windows)
        {
            if (window.Animation is not { } animation) continue;
            Changed = true;
            if (animation.IsOver(now)) window.Animation = null;
        }
        if (_typing is { } text && Find(_typingWindow) is { } typed)
        {
            var count = Math.Min(text.Length, (int)((now - _typingStart) * TypingSpeed));
            if (count != typed.Typed.Length)
            {
                typed.Typed = text[..count];
                Changed = true;
            }
            if (count >= text.Length) _typing = null;
        }
        if (_worldChanged)
        {
            _worldChanged = false;
            _world.Publish(Snapshot(now));
            _nextSystem = 0;
        }
        if (now >= _nextSystem)
        {
            _nextSystem = now + 0.5;
            // The taskbar's clock, a new minute.
            var clock = DateTime.Now;
            if (clock.Hour * 60 + clock.Minute != _minute)
            {
                _minute = clock.Hour * 60 + clock.Minute;
                Changed = true;
            }
            _world.Publish(new SystemState
            {
                Cursor = Cursor,
                UserIdleSeconds = Cursor is null ? 30 : Math.Max(0, now - _lastInput),
                ActiveWindow = Front?.Handle ?? 0,
                DarkMode = Dark,
                LocalTime = DateTime.Now,
            });
        }
        return IsAnimating ? 1.0 / 60 : Math.Max(0.02, _nextSystem - now);
    }

    private WorldSnapshot Snapshot(double now) => new(
        [new MonitorInfo(0, Bounds, WorkArea, Scale, true)],
        [.. _windows.Where(w => w.IsShown).Select(Info)],
        [new TaskbarInfo(0, Taskbar, Tray, Clock, false)],
        now);

    private WindowInfo Info(StageWindow window)
    {
        var bounds = window.Bounds(WorkArea);
        return new WindowInfo(window.Handle, bounds, window.Title, window.AppId, window.State == StageWindowState.Maximized, false, Buttons(bounds));
    }

    private static CaptionButtons Buttons(PixelRect bounds) => new(
        new PixelRect(bounds.Right - 3 * ButtonWidth, bounds.Top, bounds.Right - 2 * ButtonWidth, bounds.Top + TitleHeight),
        new PixelRect(bounds.Right - 2 * ButtonWidth, bounds.Top, bounds.Right - ButtonWidth, bounds.Top + TitleHeight),
        new PixelRect(bounds.Right - ButtonWidth, bounds.Top, bounds.Right, bounds.Top + TitleHeight));

    public WindowInfo? InfoOf(StageWindow window) => window.IsShown ? Info(window) : null;

    private void Touch()
    {
        _worldChanged = true;
        Changed = true;
    }

    // ── what happens to its windows ─────────────────────────────────────────

    /// <summary>Where its button on the taskbar is (where it minimizes to).</summary>
    private Rect TaskbarButton(StageWindow window)
    {
        var open = _windows.Where(w => w.State != StageWindowState.Closed).OrderBy(w => w.Handle).ToList();
        var index = Math.Max(0, open.IndexOf(window));
        var left = Bounds.Left + Width / 2.0 - open.Count * 20 + index * 40 + 20;
        return new Rect(left + 6, Taskbar.Top + 8, 28, 24);
    }

    private static Rect R(PixelRect r) => new(r.Left, r.Top, r.Width, r.Height);

    private Rect Drawn(StageWindow window, double now) =>
        window.Animation is { } a ? a.At(now).Rect : R(window.Bounds(WorkArea));

    public void Minimize(StageWindow window)
    {
        if (!window.IsShown) return;
        var now = _clock();
        window.Animation = new StageAnimation(now, MinimizeTime, Drawn(window, now), TaskbarButton(window), 1, 0);
        window.State = StageWindowState.Minimized;
        Touch();
    }

    public void Restore(StageWindow window)
    {
        var now = _clock();
        switch (window.State)
        {
            case StageWindowState.Minimized:
                window.State = StageWindowState.Normal;
                window.Animation = new StageAnimation(now, MinimizeTime, TaskbarButton(window), R(window.Normal), 0, 1);
                BringToFront(window);
                break;
            case StageWindowState.Maximized:
                window.Animation = new StageAnimation(now, MaximizeTime, Drawn(window, now), R(window.Normal), 1, 1);
                window.State = StageWindowState.Normal;
                break;
            case StageWindowState.Closed:
                Open(window);
                return;
            default:
                return;
        }
        Touch();
    }

    public void Maximize(StageWindow window)
    {
        if (window.State == StageWindowState.Maximized) return;
        var now = _clock();
        var from = window.State == StageWindowState.Minimized ? TaskbarButton(window) : Drawn(window, now);
        window.Animation = new StageAnimation(now, MaximizeTime, from, R(WorkArea), window.IsShown ? 1 : 0, 1);
        window.State = StageWindowState.Maximized;
        BringToFront(window);
        Touch();
    }

    public void Close(StageWindow window)
    {
        if (window.State == StageWindowState.Closed) return;
        var now = _clock();
        var from = Drawn(window, now);
        window.Animation = window.IsShown ? new StageAnimation(now, CloseTime, from, Shrunk(from), 1, 0) : null;
        window.State = StageWindowState.Closed;
        window.Typed = "";
        Touch();
    }

    /// <summary>A closed window opens again (or for the first time), in front.</summary>
    public StageWindow Open(StageWindow window)
    {
        // Already open: only to the front (opening it again would start its animation over).
        if (window.State != StageWindowState.Closed)
        {
            BringToFront(window);
            return window;
        }
        var now = _clock();
        window.Normal = window.Home;
        window.State = StageWindowState.Normal;
        window.Animation = new StageAnimation(now, OpenTime, Shrunk(R(window.Home)), R(window.Home), 0, 1);
        BringToFront(window);
        Touch();
        return window;
    }

    /// <summary>The window a program would open (the calculator): opened, in front.</summary>
    public StageWindow OpenNew() => Open(_calculator);

    /// <summary>The calculator, if open.</summary>
    public StageWindow Opened => _calculator;

    private static Rect Shrunk(Rect r) => new(r.X + r.Width * 0.04, r.Y + r.Height * 0.04, r.Width * 0.92, r.Height * 0.92);

    public void BringToFront(StageWindow window)
    {
        if (_windows.IndexOf(window) == 0) return;
        _windows.Remove(window);
        _windows.Insert(0, window);
        Touch();
    }

    /// <summary>Sends a window behind the others (quietly, between clips).</summary>
    public void SendToBack(StageWindow window)
    {
        _windows.Remove(window);
        _windows.Add(window);
        Touch();
    }

    /// <summary>Moves a window there: at once (an assistant dragging it), or quickly (what K-Desktop would do itself).</summary>
    public void Move(StageWindow window, PixelRect to, bool animate)
    {
        var now = _clock();
        if (window.State == StageWindowState.Maximized) window.State = StageWindowState.Normal;
        if (animate && window.IsShown) window.Animation = new StageAnimation(now, MoveTime, Drawn(window, now), R(to), 1, 1);
        window.Normal = to;
        Touch();
    }

    /// <summary>Where a move takes the main window: to the other side of the stage.</summary>
    public PixelRect MoveDestination(StageWindow window)
    {
        var r = window.Normal;
        var left = r.Left - Bounds.Left > Width / 2 - r.Width / 2 ? Bounds.Left + 40 : Bounds.Left + 250;
        var top = r.Top - Bounds.Top > 70 ? Bounds.Top + 48 : Bounds.Top + 78;
        return PixelRect.FromSize(left, top, r.Width, r.Height);
    }

    /// <summary>Pretends something is typed into a window, letter by letter (nothing: stops).</summary>
    public void Type(StageWindow? window, string? text)
    {
        if (window is null || text is null)
        {
            _typing = null;
            return;
        }
        _typingWindow = window.Handle;
        _typing = window.Typed + text;
        _typingStart = _clock() - window.Typed.Length / TypingSpeed;
    }

    // ── IWindowServices ─────────────────────────────────────────────────────

    public bool TryMove(long handle, PixelRect bounds)
    {
        if (Find(handle) is not { State: StageWindowState.Normal } window || bounds.IsEmpty) return false;
        if (window.Normal == bounds) return true;
        window.Animation = null;
        window.Normal = bounds;
        Touch();
        return true;
    }

    public bool TryCommand(long handle, WindowCommand command)
    {
        if (Find(handle) is not { } window || window.State == StageWindowState.Closed) return false;
        switch (command)
        {
            case WindowCommand.Minimize: Minimize(window); break;
            case WindowCommand.Maximize: Maximize(window); break;
            case WindowCommand.Restore: Restore(window); break;
            case WindowCommand.Activate:
                if (window.State == StageWindowState.Minimized) Restore(window);
                else BringToFront(window);
                break;
        }
        return true;
    }

    public CaptionButtons? GetCaptionButtons(long handle) => Find(handle) is { } window ? Buttons(window.Bounds(WorkArea)) : null;

    public ImageSource? Thumbnail(long handle, int maxSize)
    {
        if (Find(handle) is not { } window || maxSize < 1) return null;
        var bounds = window.Bounds(WorkArea);
        var scale = Math.Min(1, maxSize / (double)Math.Max(bounds.Width, bounds.Height));
        int width = Math.Max(1, (int)Math.Round(bounds.Width * scale)), height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-bounds.Left, -bounds.Top));
            DrawWindow(dc, window, R(bounds), 1, false);
            dc.Pop();
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public ImageSource? AppIcon(long handle)
    {
        if (Find(handle) is not { } window) return null;
        var group = new DrawingGroup();
        using (var dc = group.Open()) DrawAppIcon(dc, window.Kind, new Rect(0, 0, 32, 32));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static SolidColorBrush Solid(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private static Pen Line(uint argb, double width)
    {
        var pen = new Pen(Solid(argb), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static void Text(DrawingContext dc, string text, Point at, double size, Brush brush, Typeface? face = null, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face ?? Face, size, brush, 1.0);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 6;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        dc.DrawText(formatted, at);
    }

    /// <summary>The desktop under the assistants: wallpaper, windows (back to front) and taskbar, in the stage's pixels.</summary>
    public Drawing Backdrop(double now)
    {
        if (_backdrop is not null && !Changed) return _backdrop;
        Changed = false;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            DrawWallpaper(dc);
            for (var i = _windows.Count - 1; i >= 0; i--)
            {
                var window = _windows[i];
                if (window.Animation is { } animation)
                {
                    var (rect, opacity) = animation.At(now);
                    if (opacity > 0.01) DrawWindow(dc, window, rect, opacity, window.State == StageWindowState.Maximized && animation.IsOver(now));
                }
                else if (window.IsShown) DrawWindow(dc, window, R(window.Bounds(WorkArea)), 1, window.State == StageWindowState.Maximized);
            }
            DrawTaskbar(dc);
        }
        group.Freeze();
        return _backdrop = group;
    }

    /// <summary>The cursor the stage draws itself while recording, or null.</summary>
    public Drawing? CursorDrawing()
    {
        if (!DrawCursor || Cursor is not { } at) return null;
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(at, true, true);
            g.PolyLineTo([new Point(at.X, at.Y + 21), new Point(at.X + 5, at.Y + 16), new Point(at.X + 9, at.Y + 24),
                new Point(at.X + 12, at.Y + 23), new Point(at.X + 8, at.Y + 15), new Point(at.X + 15, at.Y + 15)], true, true);
        }
        arrow.Freeze();
        var drawing = new GeometryDrawing(Solid(0xFFFFFFFF), Line(0xFF101010, 1.2), arrow);
        drawing.Freeze();
        return drawing;
    }

    private void DrawWallpaper(DrawingContext dc)
    {
        var area = R(Bounds);
        var sky = new LinearGradientBrush(
            Dark ? Color.FromRgb(0x1B, 0x22, 0x3C) : Color.FromRgb(0xBF, 0xD8, 0xF2),
            Dark ? Color.FromRgb(0x30, 0x1E, 0x4A) : Color.FromRgb(0xE8, 0xD9, 0xF4), new Point(0, 0), new Point(1, 1));
        sky.Freeze();
        dc.DrawRectangle(sky, null, area);
        // Soft shapes, as on Windows' own wallpapers.
        Glow(dc, new Point(area.Left + area.Width * 0.78, area.Top + area.Height * 0.86), area.Width * 0.42, Dark ? 0x553E7BD8u : 0x66FFFFFFu);
        Glow(dc, new Point(area.Left + area.Width * 0.18, area.Top + area.Height * 0.2), area.Width * 0.3, Dark ? 0x40A05AD0u : 0x55A8C8F0u);
    }

    private static void Glow(DrawingContext dc, Point center, double radius, uint argb)
    {
        var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B))
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radius,
            RadiusY = radius,
            MappingMode = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        dc.DrawEllipse(brush, null, center, radius, radius);
    }

    private void DrawTaskbar(DrawingContext dc)
    {
        var bar = R(Taskbar);
        dc.DrawRectangle(Solid(Dark ? 0xEE1F2024 : 0xEEF3F4F7), null, bar);
        dc.DrawLine(Line(Dark ? 0x30FFFFFFu : 0x20000000u, 1), bar.TopLeft, bar.TopRight);
        var open = _windows.Where(w => w.State != StageWindowState.Closed).OrderBy(w => w.Handle).ToList();
        // The start button, then a button per open window, in the middle.
        var middle = Bounds.Left + Width / 2.0 - open.Count * 20;
        var start = new Rect(middle - 40 + 10, bar.Top + 10, 20, 20);
        foreach (var (dx, dy) in new[] { (0, 0), (11, 0), (0, 11), (11, 11) })
            dc.DrawRoundedRectangle(Solid(0xFF3C8CE6), null, new Rect(start.X + dx, start.Y + dy, 9, 9), 1.5, 1.5);
        var front = Front;
        for (var i = 0; i < open.Count; i++)
        {
            var window = open[i];
            var cell = new Rect(middle + i * 40, bar.Top + 4, 40, 32);
            if (window == front) dc.DrawRoundedRectangle(Solid(Dark ? 0x22FFFFFFu : 0x14000000u), null, new Rect(cell.X + 2, cell.Y, 36, 32), 4, 4);
            DrawAppIcon(dc, window.Kind, new Rect(cell.X + 11, cell.Y + 7, 18, 18));
            var mark = window == front ? 14 : 6;
            dc.DrawRoundedRectangle(Solid(window == front ? 0xFF3C8CE6 : 0xFF9AA0A8), null, new Rect(cell.X + 20 - mark / 2.0, cell.Bottom - 3, mark, 3), 1.5, 1.5);
        }
        // The tray (network, sound, battery) and the clock.
        var ink = Solid(Dark ? 0xFFE6E7EA : 0xFF2A2C30);
        var tray = R(Tray);
        var y = tray.Top + tray.Height / 2;
        var x = tray.Left + 14;
        var pen = Line(Dark ? 0xFFE6E7EA : 0xFF2A2C30, 1.4);
        dc.DrawGeometry(null, pen, Geometry.Parse(string.Create(CultureInfo.InvariantCulture, $"M{x - 6},{y - 1} Q{x},{y - 7} {x + 6},{y - 1} M{x - 3},{y + 2} Q{x},{y - 1} {x + 3},{y + 2}")));
        dc.DrawEllipse(ink, null, new Point(x, y + 4), 1.4, 1.4);
        x += 24;
        dc.DrawGeometry(ink, null, Geometry.Parse(string.Create(CultureInfo.InvariantCulture, $"M{x - 6},{y - 2} L{x - 3},{y - 2} L{x + 1},{y - 6} L{x + 1},{y + 6} L{x - 3},{y + 2} L{x - 6},{y + 2} Z")));
        dc.DrawGeometry(null, pen, Geometry.Parse(string.Create(CultureInfo.InvariantCulture, $"M{x + 4},{y - 3} Q{x + 6.5},{y} {x + 4},{y + 3}")));
        x += 24;
        dc.DrawRoundedRectangle(null, pen, new Rect(x - 8, y - 4, 15, 8), 1.5, 1.5);
        dc.DrawRectangle(ink, null, new Rect(x - 6, y - 2, 9, 4));
        var time = DateTime.Now;
        var clock = R(Clock);
        Text(dc, time.ToString("HH:mm", CultureInfo.InvariantCulture), new Point(clock.Left + 14, clock.Top + 4), 11, ink);
        Text(dc, time.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), new Point(clock.Left + 2, clock.Top + 19), 10, ink);
    }

    private static void DrawAppIcon(DrawingContext dc, StageKind kind, Rect r)
    {
        var (back, fore) = kind switch
        {
            StageKind.Notes => (0xFF3C8CE6u, 0xFFFFFFFFu),
            StageKind.Photos => (0xFFE0713Cu, 0xFFFFFFFFu),
            _ => (0xFF6B6F78u, 0xFFFFFFFFu),
        };
        dc.DrawRoundedRectangle(Solid(back), null, r, r.Width * 0.22, r.Height * 0.22);
        var pen = Line(fore, Math.Max(1, r.Width / 14));
        switch (kind)
        {
            case StageKind.Notes:
                for (var i = 0; i < 3; i++)
                {
                    var y = r.Top + r.Height * (0.32 + i * 0.18);
                    dc.DrawLine(pen, new Point(r.Left + r.Width * 0.26, y), new Point(r.Right - r.Width * (i == 2 ? 0.42 : 0.26), y));
                }
                break;
            case StageKind.Photos:
                dc.DrawEllipse(Solid(fore), null, new Point(r.Left + r.Width * 0.68, r.Top + r.Height * 0.32), r.Width * 0.09, r.Width * 0.09);
                var hill = new StreamGeometry();
                using (var g = hill.Open())
                {
                    g.BeginFigure(new Point(r.Left + r.Width * 0.2, r.Bottom - r.Height * 0.24), true, true);
                    g.PolyLineTo([new Point(r.Left + r.Width * 0.42, r.Top + r.Height * 0.42), new Point(r.Left + r.Width * 0.6, r.Bottom - r.Height * 0.36),
                        new Point(r.Left + r.Width * 0.68, r.Bottom - r.Height * 0.46), new Point(r.Right - r.Width * 0.2, r.Bottom - r.Height * 0.24)], true, true);
                }
                hill.Freeze();
                dc.DrawGeometry(Solid(fore), null, hill);
                break;
            default:
                for (var row = 0; row < 3; row++)
                    for (var col = 0; col < 3; col++)
                        dc.DrawRectangle(Solid(fore), null, new Rect(r.Left + r.Width * (0.24 + col * 0.19), r.Top + r.Height * (0.24 + row * 0.19), r.Width * 0.13, r.Height * 0.13));
                break;
        }
    }

    private void DrawWindow(DrawingContext dc, StageWindow window, Rect r, double opacity, bool maximized)
    {
        if (opacity < 0.999) dc.PushOpacity(opacity);
        var radius = maximized ? 0 : 7;
        if (!maximized)
        {
            for (var i = 3; i >= 1; i--)
                dc.DrawRoundedRectangle(Solid((uint)(0x0E - i * 2) << 24), null, new Rect(r.X - i, r.Y - i + 3, r.Width + 2 * i, r.Height + 2 * i), radius + i, radius + i);
        }
        var face = Solid(Dark ? 0xFF2B2D31 : 0xFFFBFBFC);
        dc.DrawRoundedRectangle(face, Line(Dark ? 0xFF44474D : 0xFFCDD1D7, 1), r, radius, radius);
        var title = new Rect(r.X, r.Y, r.Width, Math.Min(TitleHeight, r.Height));
        dc.PushClip(new RectangleGeometry(r, radius, radius));
        dc.DrawRectangle(Solid(Dark ? 0xFF232428 : 0xFFF0F2F5), null, title);
        dc.Pop();
        var ink = Solid(Dark ? 0xFFE6E7EA : 0xFF24262A);
        var faint = Dark ? 0xFF8A8E96 : 0xFF6E737B;
        DrawAppIcon(dc, window.Kind, new Rect(r.X + 9, r.Y + 8, 14, 14));
        Text(dc, window.Title, new Point(r.X + 30, r.Y + 7), 11.5, ink, Face, Math.Max(10, r.Width - 30 - 3 * ButtonWidth));
        // Caption buttons: minimize, maximize (restore when maximized), close.
        var glyph = Line(Dark ? 0xFFDADCE0 : 0xFF33363B, 1);
        var cy = r.Y + TitleHeight / 2.0;
        var bx = r.Right - 3 * ButtonWidth + ButtonWidth / 2.0;
        dc.DrawLine(glyph, new Point(bx - 5, cy), new Point(bx + 5, cy));
        bx += ButtonWidth;
        if (maximized)
        {
            dc.DrawRectangle(null, glyph, new Rect(bx - 4, cy - 2, 7, 7));
            dc.DrawLine(glyph, new Point(bx - 2, cy - 4), new Point(bx + 5, cy - 4));
            dc.DrawLine(glyph, new Point(bx + 5, cy - 4), new Point(bx + 5, cy + 3));
        }
        else dc.DrawRectangle(null, glyph, new Rect(bx - 5, cy - 5, 10, 10));
        bx += ButtonWidth;
        dc.DrawLine(glyph, new Point(bx - 5, cy - 5), new Point(bx + 5, cy + 5));
        dc.DrawLine(glyph, new Point(bx - 5, cy + 5), new Point(bx + 5, cy - 5));

        var content = new Rect(r.X + 1, r.Y + TitleHeight, Math.Max(1, r.Width - 2), Math.Max(1, r.Height - TitleHeight - 1));
        dc.PushClip(new RectangleGeometry(content));
        switch (window.Kind)
        {
            case StageKind.Notes:
                var top = content.Top + 12;
                if (window.Typed.Length > 0 || _typing is not null && _typingWindow == window.Handle)
                {
                    var typed = window.Typed + (_typing is not null && _typingWindow == window.Handle && (int)(_clock() * 2) % 2 == 0 ? "|" : "");
                    Text(dc, typed, new Point(content.Left + 14, top - 2), 12.5, ink, Face, content.Width - 28);
                    top += 22 * Math.Max(1, Math.Ceiling(window.Typed.Length * 6.5 / Math.Max(40, content.Width - 28)));
                }
                var lines = Line(faint & 0x60FFFFFF, 5);
                double[] lengths = [0.86, 0.72, 0.9, 0.55, 0.8, 0.66, 0.4];
                foreach (var length in lengths)
                {
                    if (top > content.Bottom - 10) break;
                    dc.DrawLine(lines, new Point(content.Left + 16, top + 4), new Point(content.Left + 16 + (content.Width - 32) * length, top + 4));
                    top += 18;
                }
                break;
            case StageKind.Photos:
                uint[] colors = [0xFF7FB3E8, 0xFFE8B07F, 0xFF8FD0A4, 0xFFD58FC9, 0xFFE6D27A, 0xFF8FA6D8];
                var cellWidth = (content.Width - 16 * 4) / 3;
                var cellHeight = (content.Height - 14 * 3) / 2;
                for (var i = 0; i < 6; i++)
                {
                    var cell = new Rect(content.Left + 16 + i % 3 * (cellWidth + 16), content.Top + 14 + i / 3 * (cellHeight + 14), Math.Max(1, cellWidth), Math.Max(1, cellHeight));
                    dc.DrawRoundedRectangle(Solid(colors[i]), null, cell, 4, 4);
                    dc.DrawEllipse(Solid(0xB0FFFFFF), null, new Point(cell.Right - cell.Width * 0.25, cell.Top + cell.Height * 0.3), cell.Height * 0.1, cell.Height * 0.1);
                }
                break;
            default:
                var display = new Rect(content.Left + 12, content.Top + 10, content.Width - 24, 40);
                Text(dc, "1 024", new Point(display.Right - 64, display.Top + 4), 24, ink, Bold);
                var keyWidth = (content.Width - 24 - 3 * 6) / 4;
                var keyHeight = (content.Bottom - display.Bottom - 12 - 4 * 6) / 4;
                for (var row = 0; row < 4; row++)
                    for (var col = 0; col < 4; col++)
                    {
                        var key = new Rect(content.Left + 12 + col * (keyWidth + 6), display.Bottom + 6 + row * (keyHeight + 6), Math.Max(1, keyWidth), Math.Max(1, keyHeight));
                        var accent = col == 3 && row == 3;
                        dc.DrawRoundedRectangle(Solid(accent ? 0xFF3C8CE6 : Dark ? 0xFF383B41 : 0xFFE9EBEE), null, key, 4, 4);
                    }
                break;
        }
        dc.Pop();
        if (opacity < 0.999) dc.Pop();
    }
}
