using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Localization;
using static KDesktop.Assistant.Host.Interop.Native;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace KDesktop.Assistant.Host.Input;

/// <summary>
/// Files dragged onto a hit area (stage 13.8). The hit windows live in the assistant process, which runs as the normal user
/// (medium integrity), so the Explorer can drop on them (it cannot on K-Desktop, which is elevated). Only files are taken
/// (text or links are not), always as a copy or a link: never as a move, which would let the source delete them.
/// </summary>
/// <param name="raise">Over, away or dropped, with the files and the cursor in screen pixels.</param>
/// <param name="name">The assistant's name, for the label under the dragged picture («Give it to Lander»).</param>
[ComVisible(true)]
internal sealed class FileDropTarget(nint window, Action<PointerEventKind, IReadOnlyList<string>, Point> raise, Func<string> name)
    : IDropTarget
{
    private static uint _descriptionFormat;
    private IReadOnlyList<string> _files = [];
    private IDataObject? _data;
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
        Forget();
    }

    public int DragEnter(IDataObject data, int keys, POINTL at, ref int effect)
    {
        _files = FilesOf(data);
        _data = data;
        effect = _files.Count == 0 ? DROPEFFECT_NONE : EffectFor(effect);
        var shown = effect;
        Helper(h => { var p = Point(at); h.DragEnter(window, data, ref p, shown); });
        if (_files.Count == 0) return 0;
        Describe(data, shown == DROPEFFECT_LINK ? DROPIMAGE_LINK : DROPIMAGE_COPY, Loc.T("assistant.drop.give", name()));
        raise(PointerEventKind.FilesOver, _files, new Point(at.X, at.Y));
        return 0;
    }

    public int DragOver(int keys, POINTL at, ref int effect)
    {
        effect = _files.Count == 0 ? DROPEFFECT_NONE : EffectFor(effect);
        var shown = effect;
        Helper(h => { var p = Point(at); h.DragOver(ref p, shown); });
        return 0;
    }

    public int DragLeave()
    {
        Helper(h => h.DragLeave());
        // Other windows show their own label again.
        if (_data is { } data) Describe(data, DROPIMAGE_INVALID, "");
        var files = _files;
        Forget();
        if (files.Count > 0) raise(PointerEventKind.FilesAway, files, Cursor());
        return 0;
    }

    public int Drop(IDataObject data, int keys, POINTL at, ref int effect)
    {
        var files = _files.Count > 0 ? _files : FilesOf(data);
        effect = files.Count == 0 ? DROPEFFECT_NONE : EffectFor(effect);
        var dropped = effect;
        Helper(h => { var p = Point(at); h.Drop(data, ref p, dropped); });
        Forget();
        if (files.Count > 0) raise(PointerEventKind.FilesDropped, files, new Point(at.X, at.Y));
        return 0;
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
        _data = null;
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

    /// <summary>
    /// The label under the dragged picture (a DROPDESCRIPTION in the data object): the source's drag image shows it while it
    /// is over the assistant. <see cref="DROPIMAGE_INVALID"/> gives the default back.
    /// </summary>
    private static void Describe(IDataObject data, int image, string message)
    {
        const int Chars = 260;
        if (_descriptionFormat == 0) _descriptionFormat = RegisterClipboardFormat("DropDescription");
        if (_descriptionFormat == 0) return;
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)(sizeof(int) + Chars * 2 * sizeof(char)));
        if (memory == 0) return;
        try
        {
            var target = GlobalLock(memory);
            if (target == 0)
            {
                GlobalFree(memory);
                return;
            }
            try
            {
                var bytes = new byte[sizeof(int) + Chars * 2 * sizeof(char)];
                BitConverter.TryWriteBytes(bytes.AsSpan(0, sizeof(int)), image);
                var text = message.Length >= Chars ? message[..(Chars - 1)] : message;
                System.Text.Encoding.Unicode.GetBytes(text, bytes.AsSpan(sizeof(int)));
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }
            var format = new FORMATETC
            {
                cfFormat = unchecked((short)_descriptionFormat),
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL,
            };
            var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
            data.SetData(ref format, ref medium, true); // the data object owns the memory now
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            GlobalFree(memory);
        }
    }

    private static POINT Point(POINTL at) => new() { X = at.X, Y = at.Y };

    private static Point Cursor() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;
}
