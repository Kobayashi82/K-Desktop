using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace DotAssistant;

/// <summary>
/// The minimal example assistant: a dot that teleports. It shows the whole contract in a few lines: drawing on a
/// surface only when something changes (and sleeping otherwise), a round hit area, being dragged, reacting to the
/// world, Communicate and React (the two gestures every assistant knows), its settings and its panel card.
/// </summary>
public sealed class Dot : IAssistant
{
    private const double JumpOut = 0.18, JumpIn = 0.25, ReactTime = 0.6;

    private readonly Random _random = new();
    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private Point _at;
    private Point? _jumpTo;
    private double _jumpStart = double.NaN;
    private double _nextWander;
    private (Color Color, double Start)? _reaction;
    private bool _speaking;
    private AssistantState _state;
    private Vector _grab;
    private bool _dragging;
    private bool _dirty = true;
    private bool _leaving;
    private int _jumps;
    private double _size = 28;
    private Color _color = Colors.DodgerBlue;
    private bool _wander = true;

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - Radius);

    /// <summary>The host clock, from the last tick.</summary>
    private double Now { get; set; }

    private double Scale => _host.World.Current.MonitorAt(_at)?.DpiScale ?? 1;

    private double Radius => _size * Scale / 2;

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
        _jumps = LoadJumps();
        ReadSettings(host.Settings);
    }

    public void Enter(EntranceKind how)
    {
        // Appear somewhere above the taskbar of the main monitor.
        _at = RandomSpot() ?? new Point(400, 400);
        Jump(_at);
        Publish();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        // Shrink away; the host waits for this (up to its limit) before unloading.
        _leaving = true;
        _jumpTo = null;
        _jumpStart = Now;
        _host.RequestTick();
        try { await Task.Delay(TimeSpan.FromSeconds(JumpOut), ct); } catch (OperationCanceledException) { }
    }

    public double Tick(TickInfo tick)
    {
        Now = tick.Now;
        if (_wander && !_dragging && double.IsNaN(_jumpStart) && tick.Now >= _nextWander && RandomSpot() is { } spot) Jump(spot);

        var animating = !double.IsNaN(_jumpStart) || _reaction is not null || _speaking || _state is AssistantState.Listening or AssistantState.Thinking;
        if (_dirty || animating)
        {
            _dirty = false;
            Draw(tick.Now);
        }
        // Moving: every frame. At rest: wake up only for the next wander (or rarely, if it does not wander).
        return animating ? _host.Rendering.FrameInterval : Math.Max(0.05, (_wander ? _nextWander : tick.Now + 60) - tick.Now);
    }

    private void Draw(double now)
    {
        var scale = 1.0;
        if (!double.IsNaN(_jumpStart))
        {
            var t = now - _jumpStart;
            if (_leaving) scale = Math.Max(0, 1 - t / JumpOut);
            else if (t < JumpOut) scale = 1 - t / JumpOut;
            else
            {
                if (_jumpTo is { } to)
                {
                    _at = to;
                    _jumpTo = null;
                }
                scale = Math.Min(1, (t - JumpOut) / JumpIn);
                if (t >= JumpOut + JumpIn) _jumpStart = double.NaN;
            }
        }

        var color = _color;
        var pulse = 0.0;
        if (_reaction is { } reaction)
        {
            var t = (now - reaction.Start) / ReactTime;
            if (t >= 1) _reaction = null;
            else
            {
                color = Blend(reaction.Color, _color, t);
                pulse = Math.Sin(t * Math.PI) * 0.35;
            }
        }
        if (_speaking || _state == AssistantState.Thinking) pulse = Math.Max(pulse, 0.12 + 0.12 * Math.Sin(now * 8));

        var r = Radius * scale * (1 + pulse);
        var halo = Radius * 2.2;
        var area = new PixelRect((int)(_at.X - halo), (int)(_at.Y - halo), (int)(_at.X + halo) + 1, (int)(_at.Y + halo) + 1);
        _surface.Present(area, dc =>
        {
            if (r <= 0.5) return;
            var glow = new RadialGradientBrush(Color.FromArgb(110, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
            glow.Freeze();
            dc.DrawEllipse(glow, null, _at, r * 1.9, r * 1.9);
            var body = new SolidColorBrush(color);
            body.Freeze();
            dc.DrawEllipse(body, null, _at, r, r);
            if (_state == AssistantState.Listening)
            {
                var ring = new Pen(new SolidColorBrush(Colors.White), Math.Max(1.5, r * 0.15));
                ring.Freeze();
                dc.DrawEllipse(null, ring, _at, r * 1.45, r * 1.45);
            }
        });
        var hit = Math.Max(8, Radius);
        _hit.Place(new HitShape.Ellipse(new PixelRect((int)(_at.X - hit), (int)(_at.Y - hit), (int)(_at.X + hit), (int)(_at.Y + hit))));
    }

    private void Jump(Point to)
    {
        _jumpTo = to;
        _jumpStart = Now;
        _nextWander = Now + 8 + _random.NextDouble() * 12;
        _jumps++;
        SaveJumps();
        Publish();
        _host.RequestTick();
    }

    /// <summary>A free spot just above the taskbar of a random monitor, or on top of a window.</summary>
    private Point? RandomSpot()
    {
        var world = _host.World.Current;
        var fullscreen = _host.World.System.FullscreenMonitors;
        var monitors = world.Monitors.Where(m => !fullscreen.Contains(m.Index)).ToList();
        if (monitors.Count == 0) return null;
        var windows = world.Windows.Where(w => !w.IsMaximized && monitors.Any(m => m.WorkArea.Contains(w.Bounds.Center))).ToList();
        if (windows.Count > 0 && _random.NextDouble() < 0.5)
        {
            var w = windows[_random.Next(windows.Count)];
            return new Point(w.Bounds.Left + 40 + _random.NextDouble() * Math.Max(1, w.Bounds.Width - 80), w.Bounds.Top - Radius - 2);
        }
        var monitor = monitors[_random.Next(monitors.Count)];
        var work = monitor.WorkArea;
        return new Point(work.Left + 60 + _random.NextDouble() * Math.Max(1, work.Width - 120), work.Bottom - Radius - 6);
    }

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        switch (gesture)
        {
            case Gesture.Communicate:
                // Pulse while the module shows the bubble; the module decides how long.
                _speaking = true;
                _host.RequestTick();
                try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
                _speaking = false;
                _dirty = true;
                return GestureOutcome.Committed;
            case Gesture.React react:
                React(react.Reaction);
                try { await Task.Delay(TimeSpan.FromSeconds(ReactTime), ct); } catch (OperationCanceledException) { }
                return GestureOutcome.Committed;
            default:
                return GestureOutcome.NotPerformed;
        }
    }

    private void React(Reaction reaction)
    {
        var color = reaction switch
        {
            Reaction.Success => Colors.LimeGreen,
            Reaction.Warning => Colors.Gold,
            Reaction.Failure => Colors.Crimson,
            _ => Colors.HotPink,
        };
        _reaction = (color, Now);
        _host.RequestTick();
    }

    public void SetState(AssistantState state)
    {
        _state = state;
        _dirty = true;
        _host.RequestTick();
    }

    public void OnVoiceLevel(double level) { }

    public void OnPointer(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.Click when RandomSpot() is { } spot:
                Jump(spot);
                break;
            case PointerEventKind.DoubleClick:
                React(Reaction.Celebration);
                break;
            case PointerEventKind.DragStart:
                _dragging = true;
                _grab = _at - e.Position;
                break;
            case PointerEventKind.DragMove:
                _at = e.Position + _grab;
                _dirty = true;
                _host.RequestTick();
                break;
            case PointerEventKind.DragEnd:
                _dragging = false;
                break;
        }
    }

    public void OnSystemEvent(SystemEvent e)
    {
        switch (e)
        {
            case SystemEvent.RecycleBinEmptied:
            case SystemEvent.HourStruck:
                React(Reaction.Celebration);
                break;
            case SystemEvent.NotificationShown:
                React(Reaction.Warning);
                break;
            case SystemEvent.MonitorsChanged or SystemEvent.FullscreenChanged when RandomSpot() is { } spot:
                Jump(spot);
                break;
        }
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;

    public void OnSettingsChanged(JsonElement settings)
    {
        ReadSettings(settings);
        _dirty = true;
        _host.RequestTick();
    }

    public void Dispose()
    {
        _hit.Dispose();
        _surface.Dispose();
    }

    private void ReadSettings(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return;
        if (settings.TryGetProperty("size", out var size) && size.TryGetDouble(out var s)) _size = Math.Clamp(s, 8, 96);
        if (settings.TryGetProperty("wander", out var wander) && wander.ValueKind is JsonValueKind.True or JsonValueKind.False) _wander = wander.GetBoolean();
        if (settings.TryGetProperty("color", out var color))
            _color = color.GetString() switch
            {
                "orange" => Color.FromRgb(255, 140, 0),
                "green" => Color.FromRgb(46, 204, 113),
                _ => Colors.DodgerBlue,
            };
    }

    private void Publish() => _host.PublishPanel(new JsonObject { ["jumps"] = _jumps });

    private string StatsFile => Path.Combine(_host.DataDirectory, "stats.json");

    private int LoadJumps()
    {
        try { return JsonNode.Parse(File.ReadAllText(StatsFile))?["jumps"]?.GetValue<int>() ?? 0; }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException) { return 0; }
    }

    private void SaveJumps()
    {
        try { File.WriteAllText(StatsFile, new JsonObject { ["jumps"] = _jumps }.ToJsonString()); }
        catch (IOException ex) { _host.Log.Warn("No se han podido guardar los saltos", ex); }
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
