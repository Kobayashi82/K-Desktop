using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// <c>--assistant-probe x,y</c>: a built-in circle to check the overlay without any package (diagnostics and the
/// process tests). Orange (255, 127, 0), 64 px; its hit area is only the middle 32 px, so the ring lets the mouse
/// through. A click turns it blue (and back); it can be dragged. Every pointer event goes to the log.
/// </summary>
internal sealed class ProbeAssistant(Point center) : IAssistant
{
    public static readonly Color Orange = Color.FromRgb(255, 127, 0);
    private static readonly Brush OrangeBrush = Frozen(Orange);
    private static readonly Brush BlueBrush = Frozen(Color.FromRgb(0, 120, 215));
    private const int Radius = 32;

    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private Point _center = center;
    private Vector _grab;
    private bool _blue;
    private bool _dirty = true;

    public Point? BubbleAnchor => new Point(_center.X, _center.Y - Radius);

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
    }

    public void Enter(EntranceKind how) => _dirty = true;

    public Task ExitAsync(ExitKind why, CancellationToken ct) => Task.CompletedTask;

    public double Tick(TickInfo tick)
    {
        if (_dirty)
        {
            _dirty = false;
            var area = new PixelRect((int)_center.X - Radius, (int)_center.Y - Radius, (int)_center.X + Radius, (int)_center.Y + Radius);
            _surface.Present(area, dc => dc.DrawEllipse(_blue ? BlueBrush : OrangeBrush, null, _center, Radius, Radius));
            var half = Radius / 2;
            _hit.Place(new HitShape.Ellipse(new PixelRect((int)_center.X - half, (int)_center.Y - half, (int)_center.X + half, (int)_center.Y + half)));
        }
        return 1;
    }

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

    public Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct) => Task.FromResult(GestureOutcome.NotPerformed);

    public void SetState(AssistantState state, StateDetail detail) { }

    public void OnVoiceLevel(double level) { }

    public void OnPointer(PointerEvent e)
    {
        _host.Log.Info($"puntero: {e.Kind} en {e.Area} ({e.Position.X:0},{e.Position.Y:0})");
        switch (e.Kind)
        {
            case PointerEventKind.Click:
                _blue = !_blue;
                _dirty = true;
                break;
            case PointerEventKind.DragStart:
                _grab = _center - e.Position;
                break;
            case PointerEventKind.DragMove:
                _center = e.Position + _grab;
                _dirty = true;
                break;
        }
        if (_dirty) _host.RequestTick();
    }

    public void OnSystemEvent(SystemEvent e) => _host.Log.Info($"evento: {e}");

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;

    public void OnSettingsChanged(JsonElement settings) { }

    public void Dispose()
    {
        _hit?.Dispose();
        _surface?.Dispose();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
