using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using KDesktop.Core.Model;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace KDesktop.Core.Platform;

/// <summary>What «Grabar vídeo» records: a region of a monitor, a window or a whole monitor (screen pixels).</summary>
public sealed record VideoTarget(CaptureArea Kind, ScreenRect Area, IntPtr Window = default);

/// <summary>
/// How it records (Settings › Grabación); <paramref name="Clicks"/> marks the clicks on the video. A GIF or a WebP has no sound
/// and is made <paramref name="MaxWidth"/> wide at most (0: as it is).
/// </summary>
public sealed record VideoOptions(int Fps = 30, VideoQuality Quality = VideoQuality.Normal, bool Cursor = true, AudioSource? Audio = null, float MicGain = 1,
    bool Clicks = false, VideoFormat Format = VideoFormat.Mp4, int MaxWidth = 0);

/// <summary>
/// «Grabar vídeo» (F2): Windows.Graphics.Capture on Direct3D 11, the frames to an MP4 (H.264, and AAC with the sound of
/// «Grabar audio» if any) encoded by the graphics card with Media Foundation's transcoder. The frames go out at a fixed
/// rate (the newest one each time, repeated while nothing changes) and the sound from the same clock, so they never drift
/// and a still screen never holds the sound back. Paused, neither the picture nor the sound advance.
/// </summary>
public sealed partial class ScreenRecorder : IDisposable
{
    /// <summary>Sound is read this far behind «now»: its last packets may still be on their way (as «Grabar audio»).</summary>
    private const int AudioLatency = AudioTap.Rate / 4;

    /// <summary>Sound goes to the encoder in pieces of 20 ms.</summary>
    private const int AudioChunk = AudioTap.Rate / 50;

    private readonly Lock _lock = new();
    private readonly VideoTarget _target;
    private readonly VideoOptions _options;
    private int _width, _height;
    private readonly long _interval; // Stopwatch ticks between frames

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private Windows.Graphics.SizeInt32 _poolSize;
    private ID3D11Texture2D? _latest;
    private bool _hasFrame;
    private long _framesSeen;

    private AudioTap? _tap;
    private long _audioNext;   // the tap's frame where the next piece of sound starts
    private long _audioWritten; // frames of sound given to the encoder
    private readonly Queue<(long Start, long End)> _skips = new(); // the tap's frames of each pause

    private long _start = Stopwatch.GetTimestamp();
    private long _pausedTotal;
    private long? _pausedAt;
    private long _framesWritten;
    private long _nextSlot; // the frame (of the fixed rate) that goes next
    private volatile bool _stopping;
    private long? _end; // the recorded ticks where it was stopped
    private Task? _transcode;
    private IRandomAccessStream? _stream;

    public string Path { get; }

    /// <summary>Recording, or a failure that ended it (in the log).</summary>
    public event Action<Exception>? Failed;

    public bool IsPaused => _pausedAt is not null;

    /// <summary>What has been recorded so far (pauses left out).</summary>
    public TimeSpan Length => IsAnimation && _end is { } end
        ? Stopwatch.GetElapsedTime(0, end)
        : Stopwatch.GetElapsedTime(0, RecordedTicks());

    private ScreenRecorder(VideoTarget target, string path, VideoOptions options)
    {
        _target = target;
        _options = options;
        Path = path;
        var fps = Math.Clamp(options.Fps, 5, 60);
        _interval = Stopwatch.Frequency / fps;
        // H.264 wants even sizes.
        _width = Math.Max(16, target.Area.Width & ~1);
        _height = Math.Max(16, target.Area.Height & ~1);
    }

    /// <summary>Whether this Windows can record the screen (10 version 1903 and later).</summary>
    public static bool IsSupported
    {
        get
        {
            try { return GraphicsCaptureSession.IsSupported(); }
            catch (Exception ex) when (ex is COMException or TypeLoadException or PlatformNotSupportedException) { return false; }
        }
    }

    /// <summary>Starts recording <paramref name="target"/> into <paramref name="path"/> (.mp4, or .gif / .webp as <see cref="VideoOptions.Format"/>).</summary>
    public static ScreenRecorder Start(VideoTarget target, string path, VideoOptions options)
    {
        var recorder = new ScreenRecorder(target, path, options);
        try
        {
            recorder.Begin();
        }
        catch
        {
            recorder.Dispose();
            throw;
        }
        return recorder;
    }

    private void Begin()
    {
        D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out _device, out _context).CheckError();
        // The capture, the encoder and this class use it from their own threads.
        using (var multithread = _device!.QueryInterface<ID3D11Multithread>()) multithread.SetMultithreadProtected(true);
        using (var dxgi = _device.QueryInterface<IDXGIDevice>())
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var inspectable));
            _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            Marshal.Release(inspectable);
        }

        var monitor = MonitorFromPoint(new POINT { X = _target.Area.CenterX, Y = _target.Area.Top + _target.Area.Height / 2 }, 2);
        _item = _target.Kind == CaptureArea.ActiveWindow && _target.Window != IntPtr.Zero
            ? CaptureItems.ForWindow(_target.Window)
            : CaptureItems.ForMonitor(monitor);
        PrepareClicks(monitor);
        if (_target.Kind == CaptureArea.ActiveWindow && _target.Window != IntPtr.Zero)
        {
            // A window: the size Windows gives its capture.
            _width = Math.Max(16, _item.Size.Width & ~1);
            _height = Math.Max(16, _item.Size.Height & ~1);
        }
        _latest = NewTexture();
        Clear(_latest);

        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _pool.FrameArrived += OnFrame;
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = _options.Cursor;
        CaptureItems.NoBorder(_session);
        if (IsAnimation)
        {
            BeginAnimation();
            return;
        }

        if (_options.Audio is { } audio)
        {
            _tap = new AudioTap(audio, 10, _options.MicGain);
            _audioNext = AudioTap.Now;
        }

        // What goes in says its frame rate and square pixels too, or Media Foundation leaves the encoder without a type.
        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height);
        input.FrameRate.Numerator = (uint)Math.Clamp(_options.Fps, 10, 60);
        input.FrameRate.Denominator = 1;
        input.PixelAspectRatio.Numerator = 1;
        input.PixelAspectRatio.Denominator = 1;
        var video = new VideoStreamDescriptor(input);
        var source = _tap is null
            ? new MediaStreamSource(video)
            : new MediaStreamSource(video, new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(AudioTap.Rate, AudioTap.Channels, 16)));
        source.BufferTime = TimeSpan.Zero;
        source.Starting += (_, e) =>
        {
            // The clock starts when the encoder does (preparing it takes a moment: nothing of it is lost at the start).
            lock (_lock)
            {
                _start = Stopwatch.GetTimestamp();
                if (_tap is not null) _audioNext = AudioTap.Now;
            }
            e.Request.SetActualStartPosition(TimeSpan.Zero);
        };
        source.SampleRequested += OnSampleRequested;

        var fps = (uint)Math.Clamp(_options.Fps, 10, 60);
        // As Windows' own recorder sample: an empty profile with H.264 in MP4 (CreateMp4 asks for more than a screen needs).
        var profile = new MediaEncodingProfile();
        profile.Container.Subtype = MediaEncodingSubtypes.Mpeg4;
        profile.Video.Subtype = MediaEncodingSubtypes.H264;
        profile.Video.Width = (uint)_width;
        profile.Video.Height = (uint)_height;
        profile.Video.FrameRate.Numerator = fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = Bitrate(_width, _height, (int)fps, _options.Quality);
        if (_tap is null) profile.Audio = null;
        else
        {
            // Media Foundation's AAC encoder wants 16-bit samples said in the type.
            profile.Audio = AudioEncodingProperties.CreateAac(AudioTap.Rate, AudioTap.Channels, 192_000);
            profile.Audio.BitsPerSample = 16;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _session.StartCapture();
        _transcode = Task.Run(() => TranscodeAsync(source, profile));
        Log.Info($"Grabación de vídeo: empieza {_target.Kind} {_width}×{_height} a {fps} fps ({profile.Video.Bitrate / 1000} kbps), sonido {(_options.Audio?.ToString() ?? "no")} → {Path}");
    }

    /// <summary>Bits per second: pixels × frames × a quality factor, from 1 to 80 Mbps.</summary>
    public static uint Bitrate(int width, int height, int fps, VideoQuality quality)
    {
        var perPixel = quality switch { VideoQuality.Low => 0.05, VideoQuality.High => 0.16, _ => 0.09 };
        return (uint)Math.Clamp(width * (double)height * fps * perPixel, 1_000_000, 80_000_000);
    }

    private async Task TranscodeAsync(MediaStreamSource source, MediaEncodingProfile profile)
    {
        try
        {
            File.Delete(Path);
            var folder = await StorageFolder.GetFolderFromPathAsync(System.IO.Path.GetDirectoryName(Path)!);
            var file = await folder.CreateFileAsync(System.IO.Path.GetFileName(Path), CreationCollisionOption.ReplaceExisting);
            _stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = Environment.GetEnvironmentVariable("KDESKTOP_VIDEO_SOFTWARE") != "1" };
            Log.Info("Grabación de vídeo: preparando el codificador");
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, _stream, profile);
            Log.Info($"Grabación de vídeo: codificador listo ({prepared.CanTranscode})");
            if (!prepared.CanTranscode) throw new InvalidOperationException($"No se puede codificar: {prepared.FailureReason}");
            await prepared.TranscodeAsync();
            Log.Info("Grabación de vídeo: codificación terminada");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Grabación de vídeo", ex);
            _stopping = true;
            Failed?.Invoke(ex);
        }
    }

    // ── the capture ────────────────────────────────────────────────────────

    private void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null || _stopping) return;
        try
        {
            using var source = TextureOf(frame.Surface);
            var size = frame.ContentSize;
            lock (_lock)
            {
                if (_latest is null || _context is null) return;
                // What part of the frame goes into the video, at its top left (a window that grew is cut, one that shrank leaves black).
                var (x, y) = _target.Kind == CaptureArea.ActiveWindow && _target.Window != IntPtr.Zero
                    ? (0, 0)
                    : (_target.Area.Left - MonitorOrigin().X, _target.Area.Top - MonitorOrigin().Y);
                var w = Math.Min(_width, Math.Min(size.Width, (int)source.Description.Width) - x);
                var h = Math.Min(_height, Math.Min(size.Height, (int)source.Description.Height) - y);
                if (w <= 0 || h <= 0) return;
                if (w < _width || h < _height) Clear(_latest);
                _context.CopySubresourceRegion(_latest, 0, 0, 0, 0, source, 0, new Box(x, y, 0, x + w, y + h, 1));
                _hasFrame = true;
                _framesSeen++;
            }
            if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
            {
                _poolSize = size;
                sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            }
        }
        catch (Exception ex) when (ex is COMException or SharpGen.Runtime.SharpGenException or ObjectDisposedException)
        {
            Log.Warn($"Grabación de vídeo: un fotograma no se pudo copiar ({ex.Message})");
        }
    }

    private (int X, int Y) _origin = (int.MinValue, 0);

    /// <summary>The top left of the monitor being recorded (a region is cut from it).</summary>
    private (int X, int Y) MonitorOrigin()
    {
        if (_origin.X != int.MinValue) return _origin;
        var m = ScreenCapture.MonitorAt(_target.Area.CenterX, _target.Area.Top + _target.Area.Height / 2);
        return _origin = (m.Left, m.Top);
    }

    // ── the encoder asks for samples ───────────────────────────────────────

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        try
        {
            request.Sample = request.StreamDescriptor is VideoStreamDescriptor ? NextFrame() : NextSound();
        }
        catch (Exception ex) when (ex is COMException or SharpGen.Runtime.SharpGenException or ObjectDisposedException)
        {
            Log.Warn($"Grabación de vídeo: se para ({ex.Message})");
            _stopping = true;
            request.Sample = null;
        }
    }

    /// <summary>Ticks recorded until now, pauses left out.</summary>
    private long RecordedTicks()
    {
        var now = Stopwatch.GetTimestamp();
        return now - _start - _pausedTotal - (_pausedAt is { } at ? now - at : 0);
    }

    /// <summary>The next frame at its time (waiting for it); null once it is stopping.</summary>
    private MediaStreamSample? NextFrame()
    {
        var slot = _nextSlot;
        while (!_stopping && _end is null && (RecordedTicks() < slot * _interval || !_hasFrame)) Thread.Sleep(1);
        if (_stopping || (_end is { } end && (slot * _interval >= end || !_hasFrame))) return null;
        // Behind (the encoder took longer than a frame): this one goes at the time it is now, the ones missed are skipped.
        slot = Math.Max(slot, RecordedTicks() / _interval);
        var due = slot * _interval;
        ID3D11Texture2D copy;
        lock (_lock)
        {
            copy = NewTexture();
            _context!.CopyResource(copy, _latest!);
        }
        DrawClicks(copy, due);
        var surface = SurfaceOf(copy);
        var sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, Time(due));
        sample.Duration = Time(_interval);
        sample.Processed += (_, _) =>
        {
            (surface as IDisposable)?.Dispose();
            copy.Dispose();
        };
        _framesWritten++;
        _nextSlot = slot + 1;
        return sample;
    }

    /// <summary>The next 20 ms of sound once they have been heard; null once it is stopping.</summary>
    private MediaStreamSample? NextSound()
    {
        // Stopped: until the sound reaches the end of the picture.
        if (_end is { } end && _audioWritten >= end * ((double)AudioTap.Rate / Stopwatch.Frequency)) return null;
        while (!_stopping && (_pausedAt is not null || AudioTap.Now - AudioLatency < _audioNext + AudioChunk)) Thread.Sleep(5);
        if (_stopping || _tap is null) return null;
        lock (_lock)
        {
            // Past a pause (what sounded meanwhile is not in the video).
            while (_skips.TryPeek(out var skip) && _audioNext + AudioChunk > skip.Start)
            {
                _skips.Dequeue();
                if (_audioNext < skip.End) _audioNext = skip.End;
            }
        }
        var floats = _tap.Read(_audioNext, _audioNext + AudioChunk);
        var bytes = new byte[floats.Length * 2];
        for (var i = 0; i < floats.Length; i++)
        {
            var v = (short)Math.Clamp(floats[i] * 32767f, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)v;
            bytes[i * 2 + 1] = (byte)(v >> 8);
        }
        var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), TimeSpan.FromSeconds(_audioWritten / (double)AudioTap.Rate));
        sample.Duration = TimeSpan.FromSeconds(AudioChunk / (double)AudioTap.Rate);
        _audioNext += AudioChunk;
        _audioWritten += AudioChunk;
        return sample;
    }

    private static TimeSpan Time(long stopwatchTicks) => TimeSpan.FromSeconds(stopwatchTicks / (double)Stopwatch.Frequency);

    // ── pause and stop ─────────────────────────────────────────────────────

    public void Pause(bool paused)
    {
        lock (_lock)
        {
            var now = Stopwatch.GetTimestamp();
            if (paused && _pausedAt is null)
            {
                _pausedAt = now;
            }
            else if (!paused && _pausedAt is { } at)
            {
                _pausedTotal += now - at;
                _pausedAt = null;
                if (_tap is not null)
                {
                    // The tap's frames are the same clock: what sounded from the pause to now is left out.
                    var perTick = (double)AudioTap.Rate / Stopwatch.Frequency;
                    _skips.Enqueue(((long)(at * perTick), (long)(now * perTick)));
                }
            }
        }
        Log.Info(paused ? "Grabación de vídeo: en pausa" : "Grabación de vídeo: sigue");
    }

    /// <summary>Stops and closes the file; its length (pauses left out).</summary>
    public async Task<TimeSpan> StopAsync()
    {
        var length = Length;
        if (_pausedAt is not null) Pause(false);
        // The picture ends now, and the sound when it gets here (it comes a little behind); then the file is closed.
        _end ??= RecordedTicks();
        if (IsAnimation) length = Length;
        // A GIF or a WebP is encoded as it goes: what is left of it is waited for.
        if (IsAnimation && _transcode is not null) await _transcode.ConfigureAwait(false);
        else if (_transcode is not null && await Task.WhenAny(_transcode, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != _transcode)
            Log.Warn("Grabación de vídeo: el codificador no terminaba; se corta");
        _stopping = true;
        try { _session?.Dispose(); }
        catch (COMException) { /* already gone */ }
        if (_transcode is not null) await _transcode.ConfigureAwait(false);
        Dispose();
        Log.Info($"Grabación de vídeo: {length:hh\\:mm\\:ss}, {_framesSeen} fotogramas capturados, {_framesWritten} escritos → {Path}");
        return length;
    }

    public void Dispose()
    {
        _stopping = true;
        try
        {
            _session?.Dispose();
            if (_pool is not null)
            {
                _pool.FrameArrived -= OnFrame;
                _pool.Dispose();
            }
        }
        catch (COMException) { /* already gone */ }
        _session = null;
        _pool = null;
        _stream?.Dispose();
        _stream = null;
        _tap?.Dispose();
        _tap = null;
        lock (_lock)
        {
            _latest?.Dispose();
            _latest = null;
            DisposeClicks();
            (_winrtDevice as IDisposable)?.Dispose();
            _winrtDevice = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }
    }

    // ── textures and surfaces ──────────────────────────────────────────────

    private ID3D11Texture2D NewTexture() => _device!.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)_width,
        Height = (uint)_height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
    });

    private void Clear(ID3D11Texture2D texture)
    {
        using var view = _device!.CreateRenderTargetView(texture);
        _context!.ClearRenderTargetView(view, new Color4(0, 0, 0, 1));
    }

    private static ID3D11Texture2D TextureOf(IDirect3DSurface surface)
    {
        var unknown = ((IWinRTObject)surface).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in DxgiInterfaceAccess, out var access));
        try
        {
            var iid = typeof(ID3D11Texture2D).GUID;
            unsafe
            {
                var vtable = *(IntPtr**)access;
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtable[3];
                IntPtr texture;
                Marshal.ThrowExceptionForHR(getInterface(access, &iid, &texture));
                return new ID3D11Texture2D(texture);
            }
        }
        finally
        {
            Marshal.Release(access);
        }
    }

    private static IDirect3DSurface SurfaceOf(ID3D11Texture2D texture)
    {
        using var dxgi = texture.QueryInterface<IDXGISurface>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi.NativePointer, out var inspectable));
        try { return MarshalInterface<IDirect3DSurface>.FromAbi(inspectable); }
        finally { Marshal.Release(inspectable); }
    }

    private static readonly Guid DxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT point, uint flags);
}

/// <summary>Capture items for a monitor or a window, and the yellow border off where Windows lets it.</summary>
internal static class CaptureItems
{
    private static readonly Guid Interop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Session3 = new("F2CDD966-22AE-5EA1-9596-3A289344C3BE");

    public static GraphicsCaptureItem ForMonitor(IntPtr monitor) => Create(4, monitor);

    public static GraphicsCaptureItem ForWindow(IntPtr window) => Create(3, window);

    private static unsafe GraphicsCaptureItem Create(int slot, IntPtr handle)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", Interop);
        var vtable = *(IntPtr**)factory.ThisPtr;
        var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[slot];
        var iid = ItemIid;
        IntPtr item;
        Marshal.ThrowExceptionForHR(create(factory.ThisPtr, handle, &iid, &item));
        try { return GraphicsCaptureItem.FromAbi(item); }
        finally { Marshal.Release(item); }
    }

    /// <summary>Windows 11 lets a program record without the yellow border; before it, the border stays (never in the video).</summary>
    public static unsafe void NoBorder(GraphicsCaptureSession session)
    {
        var unknown = ((IWinRTObject)session).NativeObject.ThisPtr;
        var iid = Session3;
        if (Marshal.QueryInterface(unknown, in iid, out var session3) < 0) return;
        try
        {
            var vtable = *(IntPtr**)session3;
            var setBorder = (delegate* unmanaged[Stdcall]<IntPtr, byte, int>)vtable[7];
            var hr = setBorder(session3, 0);
            if (hr < 0) Log.Info($"Grabación de vídeo: Windows deja el borde amarillo (0x{hr:X8})");
        }
        finally
        {
            Marshal.Release(session3);
        }
    }
}
