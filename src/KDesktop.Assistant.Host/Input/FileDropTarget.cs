using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace KDesktop.Assistant.Host.Input;

/// <summary>
/// Files dragged onto a hit area (stage 13.8). The hit windows live in the assistant process, which runs as the normal user
/// (medium integrity), so the Explorer can drop on them (it cannot on K-Desktop, which is elevated). Only files are taken
/// (text or links are not), always as a copy or a link: never as a move, which would let the source delete them.
/// While files are over it the shell's picture of them is not shown (it covered the assistant): the assistant gets it with
/// the events (SDK 1.17) to show it its own way, and the host draws the label («Give it to Lander») by the cursor. The
/// shell's helper only hears of drags it does not take (text, links), which keep their picture.
/// </summary>
/// <param name="raise">Over, away or dropped, with the files, the cursor in screen pixels and the dragged picture.</param>
/// <param name="label">Where the cursor is while files are over the area (for the host's label), and null when they go.</param>
[ComVisible(true)]
internal sealed class FileDropTarget(nint window, Action<PointerEventKind, IReadOnlyList<string>, Point, BitmapSource?> raise,
    Action<Point?>? label = null) : IDropTarget
{
    private IReadOnlyList<string> _files = [];
    private BitmapSource? _preview;
    /// <summary>The shell's windows with the picture, hidden while the files are over the area.</summary>
    private readonly List<nint> _pictures = [];
    private IDropTargetHelper? _helper;
    private bool _registered;

    /// <summary>Accepts drops on the window. Without OLE on this thread (an engine started without it, a test) it does nothing.</summary>
    public void Register()
    {
        if (_registered || window == 0) return;
        var result = RegisterDragDrop(window, this);
        _registered = result >= 0;
        if (!_registered) Log.Warn($"Asistente: la zona de clic no acepta archivos soltados (0x{result:X8})");
    }

    public void Revoke()
    {
        if (!_registered) return;
        _registered = false;
        RevokeDragDrop(window);
        ShowPictures();
        Forget();
    }

    public int DragEnter(IDataObject data, int keys, POINTL at, ref int effect)
    {
        _files = FilesOf(data);
        effect = _files.Count == 0 ? DROPEFFECT_NONE : EffectFor(effect);
        if (_files.Count == 0)
        {
            var shown = effect;
            Helper(h => { var p = Point(at); h.DragEnter(window, data, ref p, shown); });
            return 0;
        }
        _preview = DragPreview.Of(data);
        HidePictures();
        label?.Invoke(new Point(at.X, at.Y));
        raise(PointerEventKind.FilesOver, _files, new Point(at.X, at.Y), _preview);
        return 0;
    }

    public int DragOver(int keys, POINTL at, ref int effect)
    {
        effect = _files.Count == 0 ? DROPEFFECT_NONE : EffectFor(effect);
        if (_files.Count == 0)
        {
            var shown = effect;
            Helper(h => { var p = Point(at); h.DragOver(ref p, shown); });
            return 0;
        }
        HidePictures();
        label?.Invoke(new Point(at.X, at.Y));
        return 0;
    }

    public int DragLeave()
    {
        // The picture comes back for the other windows.
        ShowPictures();
        var files = _files;
        var preview = _preview;
        if (files.Count == 0) Helper(h => h.DragLeave());
        Forget();
        if (files.Count == 0) return 0;
        label?.Invoke(null);
        raise(PointerEventKind.FilesAway, files, Cursor(), preview);
        return 0;
    }

    public int Drop(IDataObject data, int keys, POINTL at, ref int effect)
    {
        var files = _files.Count > 0 ? _files : FilesOf(data);
        var preview = _preview ?? (files.Count > 0 ? DragPreview.Of(data) : null);
        effect = files.Count == 0 ? DROPEFFECT_NONE : EffectFor(effect);
        var dropped = effect;
        if (files.Count == 0) Helper(h => { var p = Point(at); h.Drop(data, ref p, dropped); });
        Forget();
        if (files.Count == 0) return 0;
        label?.Invoke(null);
        raise(PointerEventKind.FilesDropped, files, new Point(at.X, at.Y), preview);
        return 0;
    }

    /// <summary>
    /// Hides the shell's picture of the files (a «SysDragImage» window of the program they come from) if it is still shown:
    /// it covered the assistant, which shows it its own way. What keeps it away is not telling the shell's helper about the
    /// drag (the helper showed it again on every move, and hiding it after that flickered); this is in case a source shows
    /// it by itself. IDropTargetHelper.Show does nothing with today's layered pictures.
    /// </summary>
    private void HidePictures()
    {
        for (nint picture = 0; (picture = FindWindowEx(0, picture, "SysDragImage", null)) != 0;)
        {
            if (!IsWindowVisible(picture)) continue;
            ShowWindow(picture, SW_HIDE);
            if (!_pictures.Contains(picture)) _pictures.Add(picture);
        }
    }

    /// <summary>Gives the picture back when the files leave the area without being dropped.</summary>
    private void ShowPictures()
    {
        foreach (var picture in _pictures) ShowWindow(picture, SW_SHOWNA);
        _pictures.Clear();
    }

    /// <summary>A copy if the source allows it, if not a link; never a move (the source would delete the files).</summary>
    internal static int EffectFor(int allowed) =>
        (allowed & DROPEFFECT_COPY) != 0 ? DROPEFFECT_COPY : (allowed & DROPEFFECT_LINK) != 0 ? DROPEFFECT_LINK : DROPEFFECT_NONE;

    /// <summary>The paths in the data (CF_HDROP), or none if it brings no files.</summary>
    internal static unsafe IReadOnlyList<string> FilesOf(IDataObject data)
    {
        var format = new FORMATETC
        {
            cfFormat = CF_HDROP,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        try
        {
            if (data.QueryGetData(ref format) != 0) return [];
            data.GetData(ref format, out var medium);
            try
            {
                var drop = medium.unionmember;
                var count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
                var files = new List<string>((int)Math.Min(count, 1000));
                var buffer = new char[32768];
                fixed (char* text = buffer)
                {
                    for (uint i = 0; i < count; i++)
                    {
                        var length = DragQueryFile(drop, i, text, (uint)buffer.Length);
                        if (length > 0) files.Add(new string(text, 0, (int)length));
                    }
                }
                return files;
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }

    private void Forget()
    {
        _files = [];
        _preview = null;
        _pictures.Clear();
    }

    /// <summary>The shell's drag image (created the first time; if it cannot be, the drag goes on without it).</summary>
    private void Helper(Action<IDropTargetHelper> call)
    {
        try
        {
            _helper ??= Type.GetTypeFromCLSID(CLSID_DragDropHelper) is { } type ? Activator.CreateInstance(type) as IDropTargetHelper : null;
            if (_helper is { } helper) call(helper);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }

    private static POINT Point(POINTL at) => new() { X = at.X, Y = at.Y };

    private static Point Cursor() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;
}
