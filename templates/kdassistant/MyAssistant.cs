using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace MyAssistant;

/// <summary>
/// A new assistant. It starts with what every assistant needs: a body drawn on a surface (only when something
/// changes), a hit area, and the two gestures the module always asks for (Communicate and React). Add more gestures
/// to «traits» in Package\assistant.json as you implement them.
/// </summary>
public sealed class MyAssistant : IAssistant
{
    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private Point _at;
    private Color _color = Colors.MediumPurple;
    private double _flash = double.NaN;
    private double _now;
    private bool _dirty = true;

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - 20);

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
    }

    public void Enter(EntranceKind how)
    {
        // Sit above the taskbar of the main monitor.
        var work = _host.World.Current.PrimaryMonitor?.WorkArea ?? new PixelRect(0, 0, 800, 600);
        _at = new Point(work.Right - 200, work.Bottom - 24);
        _dirty = true;
    }

    public Task ExitAsync(ExitKind why, CancellationToken ct) => Task.CompletedTask;

    public double Tick(TickInfo tick)
    {
        _now = tick.Now;
        var flashing = !double.IsNaN(_flash) && tick.Now - _flash < 0.5;
        if (_dirty || flashing || !double.IsNaN(_flash))
        {
            if (!flashing) _flash = double.NaN;
            _dirty = false;
            var radius = flashing ? 22 : 18;
            var area = new PixelRect((int)_at.X - 24, (int)_at.Y - 24, (int)_at.X + 24, (int)_at.Y + 24);
            var brush = new SolidColorBrush(flashing ? Colors.White : _color);
            brush.Freeze();
            _surface.Present(area, dc => dc.DrawEllipse(brush, null, _at, radius, radius));
            _hit.Place(new HitShape.Ellipse(new PixelRect((int)_at.X - 18, (int)_at.Y - 18, (int)_at.X + 18, (int)_at.Y + 18)));
        }
        // Animating: every frame. At rest: a long sleep (anything that matters calls RequestTick).
        return flashing ? _host.Rendering.FrameInterval : 30;
    }

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (gesture is Gesture.React)
        {
            Flash();
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { }
            return GestureOutcome.Committed;
        }
        if (gesture is Gesture.Communicate)
        {
            // Stay visible while the module shows the bubble.
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return GestureOutcome.Committed;
        }
        return GestureOutcome.NotPerformed;
    }

    public void SetState(AssistantState state) { }

    public void OnVoiceLevel(double level) { }

    public void OnPointer(PointerEvent e)
    {
        if (e.Kind == PointerEventKind.Click) Flash();
    }

    public void OnSystemEvent(SystemEvent e)
    {
        if (e is SystemEvent.NotificationShown) Flash();
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;

    public void OnSettingsChanged(JsonElement settings) { }

    public void Dispose()
    {
        _hit.Dispose();
        _surface.Dispose();
    }

    private void Flash()
    {
        _flash = _now;
        _host.RequestTick();
    }
}
