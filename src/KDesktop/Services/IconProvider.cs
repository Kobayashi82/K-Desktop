using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop.Services;

/// <summary>Resolves and caches icons for commands, actions and QuickMenu items.</summary>
public static class IconProvider
{
    private const int Size = 48;
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Invalidate() => Cache.Clear();

    public static void Invalidate(string key) => Cache.TryRemove(key, out _);

    // ── high level ───────────────────────────────────────────────────────

    public static ImageSource? ForMenuItem(QuickMenuItem item, Command? command)
    {
        if (item.Icon is { } own && FromConfigIcon(own) is { } img) return img;
        return item.Kind == QuickMenuItemKind.Command ? ForCommand(command) : null;
    }

    public static ImageSource? ForCommand(Command? command)
    {
        if (command is null) return Asset("Accion_Comandos");
        if (command.Icon is { } own && FromConfigIcon(own) is { } img) return img;
        return command.PrimaryAction is { } action ? ForAction(action) : Asset("Accion_Comandos");
    }

    public static ImageSource? ForAction(CommandAction action) => action switch
    {
        RunAppAction a => PathIcon(a.Path) ?? (LooksLikeUrl(a.Path) ? Asset("WebPage") : Asset("Accion_Aplicacion")),
        OpenFolderAction { Special: { } s } => Asset(SpecialFolders.Get(s)?.Icon ?? "Accion_AbrirCarpeta"),
        OpenFolderAction a => PathIcon(a.Path) ?? Asset("Accion_AbrirCarpeta"),
        OpenUrlAction a => Asset(Browsers.Get(a.Browser)?.Icon ?? "WebPage"),
        AppControlAction => Asset("Accion_Aplicaciones"),
        RunScriptAction => Asset("Accion_BAT"),
        SendInputAction => Asset("Accion_EnviarPulsacion"),
        VolumeAction => Asset("Accion_Volumen"),
        PlaySoundAction => Asset("Accion_ReproducirSonido"),
        TopMostAction => Asset("Accion_TopMost"),
        HiddenFilesAction => Asset("Accion_Archivos_Ocultos"),
        ToggleDisabledInAction => Asset("Accion_Desactivar"),
        ShowMessageAction => Asset("Accion_Mensaje"),
        PowerAction => Asset("Accion_Apagar_Equipo"),
        OpenQuickMenuAction => Asset("Accion_QuickMenu"),
        CommandControlAction => Asset("Accion_Comandos"),
        WaitAction => Asset("Accion_Esperar"),
        PlayMacroAction => Asset("Accion_Macros"),
        _ => null,
    };

    public static ImageSource? FromConfigIcon(string icon) =>
        AppPaths.ResolveIcon(icon) is { } path ? FromFile(path) : null;

    private static ImageSource? PathIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathRooted(p) && !p.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        return File.Exists(p) || Directory.Exists(p) ? Shell(p) : null;
    }

    private static bool LooksLikeUrl(string s) => s.Contains("://", StringComparison.Ordinal) || s.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    // ── sources ──────────────────────────────────────────────────────────

    /// <summary>Bundled icon from Assets/Icons (name without extension).</summary>
    public static ImageSource? Asset(string name) => Cache.GetOrAdd("asset:" + name, _ =>
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/KDesktop;component/Assets/Icons/{name}.ico", UriKind.Absolute);
            return BestFrame(BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
        }
        catch { return null; }
    });

    /// <summary>Image file (.ico, .png, .jpg…).</summary>
    public static ImageSource? FromFile(string path) => Cache.GetOrAdd("file:" + path, _ =>
    {
        try
        {
            using var stream = File.OpenRead(path);
            return BestFrame(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
        }
        catch { return null; }
    });

    /// <summary>Icon that Windows shows for a file or folder (shortcuts resolved, no arrow overlay).</summary>
    public static ImageSource? Shell(string path) => Cache.GetOrAdd("shell:" + path, _ => ExtractShellIcon(path));

    private static ImageSource? BestFrame(BitmapDecoder decoder)
    {
        BitmapFrame? best = null;
        foreach (var f in decoder.Frames)
        {
            if (best is null) { best = f; continue; }
            var score = Math.Abs(f.PixelWidth - Size) - (f.Format.BitsPerPixel >= 32 ? 1 : 0);
            var bestScore = Math.Abs(best.PixelWidth - Size) - (best.Format.BitsPerPixel >= 32 ? 1 : 0);
            if (score < bestScore) best = f;
        }
        best?.Freeze();
        return best;
    }

    private static ImageSource? ExtractShellIcon(string path)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            try
            {
                if (factory.GetImage(new SIZE { cx = Size, cy = Size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbm) != 0 || hbm == IntPtr.Zero)
                    return null;
                try { return FromDibSection(hbm); }
                finally { DeleteObject(hbm); }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Converts a 32-bpp DIB section to a BitmapSource keeping the alpha channel.</summary>
    private static BitmapSource? FromDibSection(IntPtr hbm)
    {
        var ds = new DIBSECTION();
        if (GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), ref ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
            return Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
        var pixels = new byte[stride * h];
        var bottomUp = ds.dsBmih.biHeight > 0;
        for (var y = 0; y < h; y++)
        {
            var src = ds.dsBm.bmBits + (bottomUp ? h - 1 - y : y) * stride;
            Marshal.Copy(src, pixels, y * stride, stride);
        }
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { hasAlpha = true; break; }

        var bmp = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    // ── interop ──────────────────────────────────────────────────────────

    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int size, ref DIBSECTION obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr h);
}
