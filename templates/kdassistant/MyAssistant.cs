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
    private Color _flashColor = Colors.White;
    // A few ways to react, so it does not do the same every time: by weight, never twice in a row, the rare one rests.
    private readonly Variety<Color> _reactions = new Variety<Color>()
        .Add(Colors.White, weight: 2)
        .Add(Colors.Gold)
        .Add(Colors.HotPink, weight: 0.5, cooldown: 60);
    private double _flash = double.NaN;
    private bool _flashPending;
    private bool _dirty = true;

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - 20 * _host.Common.Size);

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
        // Startup and Summoned deserve an entrance (here, a flash); Reloaded comes back quietly, where it was.
        if (how != EntranceKind.Reloaded) Flash();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        // Shutdown and Reloading: leave at once. Dismissed and Uninstalling: a goodbye (the host waits up to 3 s).
        if (why is ExitKind.Shutdown or ExitKind.Reloading) return;
        Flash();
        try { await Task.Delay(500, ct); } catch (OperationCanceledException) { }
    }

    public double Tick(TickInfo tick)
    {
        if (_flashPending)
        {
            _flashPending = false;
            _flash = tick.Now;
        }
        var flashing = !double.IsNaN(_flash) && tick.Now - _flash < 0.5;
        if (_dirty || flashing || !double.IsNaN(_flash))
        {
            if (!flashing) _flash = double.NaN;
            _dirty = false;
            // The size the user chose in the «General» tab of its card (1 = as designed; "common" in assistant.json).
            var size = _host.Common.Size;
            var radius = (flashing ? 22 : 18) * size;
            var edge = (int)Math.Ceiling(24 * size);
            var area = new PixelRect((int)_at.X - edge, (int)_at.Y - edge, (int)_at.X + edge, (int)_at.Y + edge);
            var brush = new SolidColorBrush(flashing ? _flashColor : _color);
            brush.Freeze();
            _surface.Present(area, dc => dc.DrawEllipse(brush, null, _at, radius, radius));
            var body = (int)(18 * size);
            _hit.Place(new HitShape.Ellipse(new PixelRect((int)_at.X - body, (int)_at.Y - body, (int)_at.X + body, (int)_at.Y + body)));
        }
        // Animating: every frame. At rest: a long sleep (anything that matters calls RequestTick).
        return flashing ? _host.Rendering.FrameInterval : 30;
    }

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (gesture is Gesture.React)
        {
            Flash(_reactions.Pick());
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

    public void OnSettingsChanged(JsonElement settings)
    {
        // Its settings.json changed, or the common settings (its size): draw again.
        _dirty = true;
        _host.RequestTick();
    }

    public void Dispose()
    {
        _hit.Dispose();
        _surface.Dispose();
    }

    private void Flash(Color? color = null)
    {
        _flashColor = color ?? Colors.White;
        _flashPending = true;
        _host.RequestTick();
    }
}
