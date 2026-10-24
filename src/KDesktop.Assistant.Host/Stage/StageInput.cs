using System.Windows;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Stage;

/// <summary>
/// The mouse on the stage (phase 16): K-Desktop sends where the pointer is over the stage's picture and its left button
/// (<c>stage</c>), and this makes the events a hit area would get from Windows (enter, leave, down, up, click, double
/// click, drag) out of them, for the areas without windows of the assistants on it. Engine thread.
/// </summary>
internal sealed class StageInput(StageScene scene, Func<IEnumerable<VirtualHitArea>> areas)
{
    /// <summary>How far the pointer must go with the button down to start a drag (pixels).</summary>
    private const double DragDistance = 4;

    private const double DoubleClickTime = 0.5;

    private VirtualHitArea? _hover;
    private VirtualHitArea? _pressed;
    private Point _pressAt;
    private bool _dragging;
    private VirtualHitArea? _lastClicked;
    private long _lastClick;

    public void OnStage(StageMessage message)
    {
        var at = new Point(scene.Bounds.Left + message.X, scene.Bounds.Top + message.Y);
        switch (message.Op)
        {
            case StageMessage.Cursor:
                Move(at);
                break;
            case StageMessage.Leave:
                Leave(at);
                break;
            case StageMessage.Down:
                Move(at);
                Down(at);
                break;
            case StageMessage.Up:
                Move(at);
                Up(at);
                break;
        }
    }

    private void Move(Point at)
    {
        var moved = scene.Cursor != at;
        scene.Cursor = at;
        if (_pressed is { } pressed)
        {
            if (!_dragging && (at - _pressAt).Length > DragDistance)
            {
                _dragging = true;
                pressed.Raise(PointerEventKind.DragStart, at);
            }
            // Letting go where it already was is no move.
            else if (_dragging && moved) pressed.Raise(PointerEventKind.DragMove, at);
            return;
        }
        Hover(at);
    }

    /// <summary>The area under the pointer now: leaving the one before, entering this one.</summary>
    private void Hover(Point at)
    {
        var hit = areas().FirstOrDefault(a => a.Contains(at));
        if (hit == _hover) return;
        _hover?.Raise(PointerEventKind.Leave, at);
        _hover = hit;
        hit?.Raise(PointerEventKind.Enter, at);
    }

    private void Leave(Point at)
    {
        if (_pressed is not null) Up(at);
        scene.Cursor = null;
        _hover?.Raise(PointerEventKind.Leave, at);
        _hover = null;
    }

    private void Down(Point at)
    {
        if (_hover is not { } area) return;
        _pressed = area;
        _pressAt = at;
        _dragging = false;
        area.Raise(PointerEventKind.Down, at);
    }

    private void Up(Point at)
    {
        if (_pressed is not { } area) return;
        _pressed = null;
        area.Raise(PointerEventKind.Up, at);
        if (_dragging)
        {
            _dragging = false;
            area.Raise(PointerEventKind.DragEnd, at);
            // Let go somewhere else: what is under the pointer now.
            Hover(at);
            return;
        }
        var now = Environment.TickCount64;
        if (_lastClicked == area && now - _lastClick < DoubleClickTime * 1000)
        {
            _lastClicked = null;
            area.Raise(PointerEventKind.DoubleClick, at);
            return;
        }
        _lastClicked = area;
        _lastClick = now;
        area.Raise(PointerEventKind.Click, at);
    }
}
