using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>
/// «Lupa»: enlarges the main monitor around the mouse with Windows' full-screen magnification, following it. The zoom
/// is anchored at the pointer: what is under it stays under it, so a click lands on what is seen there (the same as
/// without zoom). On a thread of its own (the API wants the thread that started it).
/// </summary>
public sealed class Magnifier : IDisposable
{
    public const double MinZoom = 1.25, MaxZoom = 8;

    private readonly Lock _lock = new();
    private Thread? _thread;
    private volatile bool _stop;
    private double _zoom = 2;

    /// <summary>The zoom was turned on or off, or changed (the web app shows it).</summary>
    public event Action? Changed;

    public bool IsOn => _thread is not null;

    public double Zoom => _zoom;

    /// <summary>Turns it on at <paramref name="zoom"/> (or changes the zoom if it is on).</summary>
    public void On(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        lock (_lock)
        {
            if (_thread is null)
            {
                _stop = false;
                _thread = new Thread(Run) { IsBackground = true, Name = "Lupa" };
                _thread.Start();
                Log.Info($"Lupa: ×{_zoom:0.##}");
            }
        }
        Changed?.Invoke();
    }

    public void Off()
    {
        Thread? thread;
        lock (_lock)
        {
            thread = _thread;
            _thread = null;
            _stop = true;
        }
        if (thread is null) return;
        thread.Join(TimeSpan.FromSeconds(2));
        Log.Info("Lupa: apagada");
        Changed?.Invoke();
    }

    /// <summary>On or off; returns whether it is on now.</summary>
    public bool Toggle(double zoom)
    {
        if (IsOn) Off();
        else On(zoom);
        return IsOn;
    }

    /// <summary>One step more or less zoom (×1.25 each), turning it on if needed.</summary>
    public void Step(int direction)
    {
        var next = direction > 0 ? _zoom * 1.25 : _zoom / 1.25;
        if (!IsOn && direction > 0) On(Math.Max(next, MinZoom));
        else if (next < MinZoom - 0.01) Off();
        else On(next);
    }

    /// <summary>
    /// Where the magnified view starts so that <paramref name="cursor"/> stays where it is: a point p shows at
    /// (p − offset) × zoom, and for the cursor that must be the cursor itself.
    /// </summary>
    public static (double X, double Y) Offset(int cursorX, int cursorY, double zoom) =>
        (cursorX * (1 - 1 / zoom), cursorY * (1 - 1 / zoom));

    private void Run()
    {
        if (!MagInitialize())
        {
            Log.Warn("Lupa: Windows no permitió la ampliación");
            lock (_lock) _thread = null;
            Changed?.Invoke();
            return;
        }
        try
        {
            var width = GetSystemMetrics(0);
            var height = GetSystemMetrics(1);
            (int X, int Y) last = (int.MinValue, int.MinValue);
            var lastZoom = 0.0;
            while (!_stop)
            {
                GetCursorPos(out var p);
                var zoom = _zoom;
                var onMain = p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height;
                if ((p.X, p.Y) != last || Math.Abs(zoom - lastZoom) > 0.001)
                {
                    if (onMain)
                    {
                        var (x, y) = Offset(p.X, p.Y, zoom);
                        MagSetFullscreenTransform((float)zoom, (int)Math.Round(x), (int)Math.Round(y));
                    }
                    else MagSetFullscreenTransform(1f, 0, 0); // the full-screen zoom is only the main monitor's
                    last = (p.X, p.Y);
                    lastZoom = zoom;
                }
                Thread.Sleep(10);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Lupa", ex);
        }
        finally
        {
            MagSetFullscreenTransform(1f, 0, 0);
            MagUninitialize();
        }
    }

    public void Dispose() => Off();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("magnification.dll")] private static extern bool MagInitialize();
    [DllImport("magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("magnification.dll")] private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
