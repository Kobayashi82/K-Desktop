using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Input;

/// <summary>
/// Keeps the drags of an assistant's body from it while the user does not let it be picked up (the common setting
/// «dragging», stage 13.13). A drag is kept or let through whole, by how things were when it started: changing the
/// setting halfway never leaves it with a start and no end. Other hit areas (a toy) are always let through.
/// </summary>
internal sealed class DragGate
{
    private bool _keeping;

    public bool LetsThrough(PointerEvent e, bool dragging)
    {
        if (e.Kind == PointerEventKind.DragStart) _keeping = !dragging && e.Area == HostPointer.BodyArea;
        if (!_keeping || e.Kind is not (PointerEventKind.DragStart or PointerEventKind.DragMove or PointerEventKind.DragEnd)) return true;
        if (e.Kind == PointerEventKind.DragEnd) _keeping = false;
        return false;
    }
}
