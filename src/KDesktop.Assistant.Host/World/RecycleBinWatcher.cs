using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Notices when the recycle bin is emptied (from Kobii). It does not poll (that would wake the disks): the shell tells
/// the engine's window when something changes in it and, once it settles, what it holds is counted. If it held
/// something and now holds nothing, it was emptied. Counting happens on another thread: with a sleeping disk it can
/// take a while, and the assistant must not freeze meanwhile.
/// </summary>
internal sealed unsafe class RecycleBinWatcher : IDisposable
{
    /// <summary>The message the shell sends to the window.</summary>
    public const uint Message = WM_APP + 2;

    /// <summary>It is counted once it has not changed for this long (emptying a full bin gives many notices).</summary>
    private const double QuietSeconds = 1;

    /// <summary>Even if it keeps changing, it is counted after this long at most.</summary>
    private const double MaxWaitSeconds = 6;

    private readonly Action<Action> _post;
    private nint _pidl;
    private uint _registration;
    private long _items = -1;
    private double _firstChange = double.NaN;
    private double _countAt = double.PositiveInfinity;
    private bool _counting;
    private bool _again;

    /// <param name="hwnd">The window that receives the notices (on the engine thread).</param>
    /// <param name="post">To come back to the engine thread with the result.</param>
    public RecycleBinWatcher(nint hwnd, Action<Action> post)
    {
        _post = post;
        if (SHGetSpecialFolderLocation(0, CSIDL_BITBUCKET, out _pidl) != 0 || _pidl == 0)
        {
            Log.Warn("Asistente: no se encuentra la papelera");
            return;
        }
        var entry = new SHChangeNotifyEntry { pidl = _pidl, fRecursive = 1 };
        _registration = SHChangeNotifyRegister(hwnd, SHCNRF_ShellLevel | SHCNRF_InterruptLevel | SHCNRF_NewDelivery, SHCNE_ALLEVENTS, Message, 1, &entry);
        if (_registration == 0)
        {
            Log.Warn("Asistente: SHChangeNotifyRegister falló; no se notará cuando se vacíe la papelera");
            return;
        }
        Count(); // what it holds at the start (once; afterwards only when it changes)
    }

    /// <summary>It was emptied (on the engine thread).</summary>
    public event Action? Emptied;

    /// <summary>The shell's notice (<see cref="Message"/>) reached the window.</summary>
    public void OnMessage(nint wParam, nint lParam, double now)
    {
        // With «NewDelivery» the data is in shared memory: open it and let it go.
        nint* pidls;
        var handle = SHChangeNotification_Lock(wParam, (uint)lParam, &pidls, out _);
        if (handle != 0) SHChangeNotification_Unlock(handle);
        if (double.IsNaN(_firstChange)) _firstChange = now;
        _countAt = Math.Min(now + QuietSeconds, _firstChange + MaxWaitSeconds);
    }

    /// <summary>Counts what it holds once it has settled.</summary>
    public void Update(double now)
    {
        if (now < _countAt) return;
        _countAt = double.PositiveInfinity;
        _firstChange = double.NaN;
        Count();
    }

    /// <summary>Seconds until it has to count (to wake the engine in time), or infinity.</summary>
    public double CountIn(double now) => _countAt - now;

    public void Dispose()
    {
        if (_registration != 0)
        {
            SHChangeNotifyDeregister(_registration);
            _registration = 0;
        }
        if (_pidl != 0)
        {
            ILFree(_pidl);
            _pidl = 0;
        }
    }

    private void Count()
    {
        if (_counting)
        {
            _again = true;
            return;
        }
        _counting = true;
        var thread = new Thread(() =>
        {
            var items = ReadItems();
            _post(() => OnCounted(items));
        })
        { IsBackground = true, Name = "K-Desktop assistant recycle bin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void OnCounted(long items)
    {
        _counting = false;
        var before = _items;
        if (items >= 0)
        {
            _items = items;
            if (before > 0 && items == 0) Emptied?.Invoke();
        }
        if (_again)
        {
            _again = false;
            Count();
        }
    }

    private static long ReadItems()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)sizeof(SHQUERYRBINFO) };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64NumItems : -1;
        }
        catch (Exception ex)
        {
            Log.Warn($"Asistente: no se ha podido contar lo que hay en la papelera ({ex.Message})");
            return -1;
        }
    }
}
