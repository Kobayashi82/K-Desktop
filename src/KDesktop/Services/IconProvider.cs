using System.Collections.Concurrent;
using System.ComponentModel;
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
public static partial class IconProvider
{
    private const int Size = 48;
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Invalidate() => Cache.Clear();

    public static void Invalidate(string key) => Cache.TryRemove(key, out _);

    /// <summary>
    /// Changes when icons that were missing become available (downloaded favicons) or stop being used. Icon bindings
    /// include it, as <c>{Binding Path=(s:IconProvider.Revision)}</c>, so they refresh. UI thread only.
    /// </summary>
    public static int Revision { get; private set; }

    public static event EventHandler<PropertyChangedEventArgs>? StaticPropertyChanged;

    public static void BumpRevision()
    {
        Revision++;
        StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(Revision)));
    }

    // ── high level ───────────────────────────────────────────────────────

    public static ImageSource? ForMenuItem(QuickMenuItem item, Command? command)
    {
        if (item.Icon is { } own && FromConfigIcon(own) is { } img) return img;
        return item.Kind == QuickMenuItemKind.Command ? ForCommand(command) : null;
    }

    public static ImageSource? ForCommand(Command? command)
    {
        if (command is null) return Kd("Flash24", "gold");
        if (command.Icon is { } own && FromConfigIcon(own) is { } img) return img;
        return command.PrimaryAction is { } action ? ForAction(action) : Kd("Flash24", "gold");
    }

    /// <summary>The event's own icon, or the one of its type (a clock for «a una hora»…).</summary>
    public static ImageSource? ForEvent(EventRule rule) =>
        (rule.Icon is { } own ? FromConfigIcon(own) : null) ?? FromConfigIcon(EventCatalog.Get(rule.Trigger.Kind).Icon);

    public static ImageSource? ForAction(CommandAction action) => action switch
    {
        RunAppAction a => PathIcon(a.Path) ?? (LooksLikeUrl(a.Path) ? ForTheme(Favicons.Get(a.Path)) ?? Kd("Globe24", "sky") : Kd("Open24", "blue")),
        OpenFolderAction { Special: { } s } => Named(SpecialFolders.Get(s)?.Icon) ?? Kd("Folder24", "yellow"),
        OpenFolderAction a => PathIcon(a.Path) ?? Kd("Folder24", "yellow"),
        OpenUrlAction a => ForTheme(Favicons.Get(a.Url)) ?? (Browsers.Get(a.Browser)?.Icon is { } browser ? Asset(browser) : Kd("Globe24", "sky")),
        AppControlAction => Kd("WindowApps24", "indigo"),
        RunScriptAction => Kd("WindowConsole20", "dark"),
        HttpRequestAction => Kd("GlobeArrowForward24", "teal"),
        SendInputAction => Kd("Keyboard24", "sky"),
        VolumeAction { Target: VolumeTarget.Microphone } => Kd("Mic24", "teal"),
        VolumeAction { Target: VolumeTarget.App or VolumeTarget.ForegroundApp } => Kd("SoundSource24", "teal"),
        VolumeAction => Kd("Speaker224", "teal"),
        AudioOutputAction => Kd("HeadphonesSoundWave24", "teal"),
        PlaySoundAction => Kd("MusicNote224", "pink"),
        TopMostAction => Kd("Pin24", "orange"),
        HiddenFilesAction => Kd("EyeOff24", "gray"),
        ToggleDisabledInAction => Kd("Prohibited24", "red"),
        ShowMessageAction => Kd("Chat24", "green"),
        PowerAction => Kd("Power24", "red"),
        OpenQuickMenuAction => Kd("TextBulletListTree24", "gold"),
        LauncherAction => Kd("Search24", "gold"),
        CommandControlAction => Kd("Flash24", "gold"),
        WaitAction => Kd("Timer24", "purple"),
        PlayMacroAction => Kd("Record24", "red"),
        KBoardAction => Kd("KeyboardLayoutFloat24", "indigo"),
        WindowAction => Kd("WindowNew24", "blue"),
        ClipboardAction => Kd("ClipboardTextEdit24", "sky"),
        WakeOnLanAction => Kd("DesktopPulse24", "green"),
        DisplayAction { Operation: DisplayOperation.Brightness } => Kd("BrightnessHigh24", "yellow"),
        DisplayAction => Kd("Desktop24", "indigo"),
        PowerPlanAction => Kd("Battery1024", "lime"),
        WindowsThemeAction => Kd("DarkTheme24", "purple"),
        RadioAction { Radio: RadioDevice.Bluetooth } => Kd("Bluetooth24", "blue"),
        RadioAction => Kd("Wifi124", "teal"),
        ConditionAction => Kd("BranchFork24", "orange"),
        ScreenshotAction => Kd("Screenshot24", "sky"),
        AudioRecordAction { Mode: RecordMode.SaveReplay } => Kd("History24", "red"),
        AudioRecordAction => Kd("MicRecord24", "red"),
        MagnifierAction => Kd("ZoomIn24", "indigo"),
        _ => null,
    };

    /// <summary>
    /// Icon stored in the configuration: "kd:Folder24:yellow" (K-Desktop collection), "imageres.dll,109" (icon of a
    /// Windows library or program, by index), or an image file (name inside the Icons folder or absolute path).
    /// </summary>
    public static ImageSource? FromConfigIcon(string icon, bool adaptToTheme = true)
    {
        if (KdIconSpec.TryParse(icon, out var kd)) return KdIcons.Render(kd);
        var image = TryParseLibrary(icon, out var file, out var index) ? Library(file, index)
            : AppPaths.ResolveIcon(icon) is { } path ? FromFile(path) : null;
        return adaptToTheme ? ForTheme(image) : image;
    }

    /// <summary>K-Desktop icon ("kd:…") or bundled asset name.</summary>
    private static ImageSource? Named(string? key) =>
        key is null ? null : KdIconSpec.TryParse(key, out var kd) ? KdIcons.Render(kd) : Asset(key);

    public static ImageSource? Kd(string symbol, string color) => KdIcons.Render(new KdIconSpec(symbol, color));

    private static ImageSource? PathIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathRooted(p) && !p.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        // Folders with a custom icon are often white glyphs (see ForTheme); file icons are Windows-drawn and left alone.
        return File.Exists(p) ? Shell(p) : Directory.Exists(p) ? ForTheme(Shell(p)) : null;
    }

    public static bool LooksLikeUrl(string s) => s.Contains("://", StringComparison.Ordinal) || s.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

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

    // ── icon libraries (shell32.dll, imageres.dll, any exe…) ─────────────

    private static readonly string[] LibraryExtensions = [".dll", ".exe", ".cpl", ".icl", ".ocx", ".scr", ".mun", ".ico"];

    /// <summary>"imageres.dll,109" / "C:\App\app.exe,2" → file and index.</summary>
    public static bool TryParseLibrary(string? value, out string file, out int index)
    {
        file = string.Empty;
        index = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var comma = value.LastIndexOf(',');
        if (comma <= 0 || !int.TryParse(value[(comma + 1)..].Trim(), out index)) return false;
        file = value[..comma].Trim().Trim('"');
        return LibraryExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Value stored for an icon of a library: short name for the System32 ones ("shell32.dll,15").</summary>
    public static string LibraryReference(string file, int index)
    {
        var full = ResolveLibrary(file);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var name = Path.GetDirectoryName(full)?.Equals(system, StringComparison.OrdinalIgnoreCase) == true ? Path.GetFileName(full) : full;
        return $"{name},{index}";
    }

    public static string ResolveLibrary(string file)
    {
        var f = Environment.ExpandEnvironmentVariables(file);
        return Path.IsPathRooted(f) ? f : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), f);
    }

    /// <summary>Number of icons inside a library or program.</summary>
    public static int LibraryCount(string file)
    {
        try { return (int)ExtractIconEx(ResolveLibrary(file), -1, null, null, 0); }
        catch { return 0; }
    }

    /// <summary>Icon number <paramref name="index"/> of a library, at 48 px.</summary>
    public static ImageSource? Library(string file, int index) => Cache.GetOrAdd($"lib:{file},{index}", _ =>
    {
        try
        {
            if (SHDefExtractIcon(ResolveLibrary(file), index, 0, out var large, out var small, (uint)(Size | (16 << 16))) != 0 || large == IntPtr.Zero)
                return null;
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, out IntPtr small, uint iconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint icons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr h);
}
