using System.Collections.Concurrent;
using System.Diagnostics;
using KDesktop.Assistant.Host.Interop;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Engine;

/// <summary>
/// The engine thread (STA), where the assistant, the overlay and the world live. It sleeps in
/// <c>MsgWaitForMultipleObjectsEx</c> on a high-resolution timer and the order queue, so it answers Windows messages at
/// once and costs nothing between ticks. Awaits started on it
/// come back to it (<see cref="SynchronizationContext"/>).
/// </summary>
internal sealed class EngineLoop : IDisposable
{
    /// <summary>The fastest pace without a compositor clock (and the cap with one).</summary>
    public const double MinInterval = 1.0 / 165;

    private readonly ConcurrentQueue<Action> _orders = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _tickSoon;
    private nint _timer;
    private double _nextTick;
    private double _lastTick;

    /// <summary>
    /// One tick: gets the time (seconds on <see cref="Now"/>) and returns how long it may sleep before the next one.
    /// Runs on the engine thread.
    /// </summary>
    public Func<double, double>? Tick { get; set; }

    /// <summary>
    /// When the next tick goes, given the time and the interval the tick asked for (default: right after it). The
    /// assistant runtime puts frame-rate ticks right after a composition of the desktop.
    /// </summary>
    public Func<double, double, double>? Schedule { get; set; }

    /// <summary>Seconds on the engine's monotonic clock.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    public int ThreadId { get; private set; }

    public bool IsEngineThread => Environment.CurrentManagedThreadId == ThreadId;

    /// <summary>Runs once on the engine thread before the first tick.</summary>
    public event Action? Started;

    /// <summary>Runs once on the engine thread after the last tick (disposing what lives there).</summary>
    public event Action? Stopping;

    public void Start()
    {
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "K-Desktop assistant engine" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Runs <paramref name="action"/> on the engine thread. Any thread.</summary>
    public void Post(Action action)
    {
        _orders.Enqueue(action);
        _wake.Set();
    }

    /// <summary>Asks for a tick as soon as possible. Any thread.</summary>
    public void RequestTick()
    {
        _tickSoon = true;
        _wake.Set();
    }

    /// <summary>Stops the loop and waits for it (up to <paramref name="timeout"/>).</summary>
    public bool Stop(TimeSpan timeout)
    {
        if (_thread is null) return true;
        _running = false;
        _wake.Set();
        var ended = _thread.Join(timeout);
        if (!ended) Log.Warn("Asistente: el hilo del motor no terminó a tiempo");
        _thread = null;
        return ended;
    }

    public void Dispose()
    {
        Stop(TimeSpan.FromSeconds(3));
        _wake.Dispose();
    }

    private void Run()
    {
        ThreadId = Environment.CurrentManagedThreadId;
        SynchronizationContext.SetSynchronizationContext(new EngineContext(this));
        _timer = CreateWaitableTimerEx(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (_timer == 0) _timer = CreateWaitableTimerEx(0, null, 0, TIMER_ALL_ACCESS);
        // OLE (not just COM) for files dragged onto the hit windows, which live here (stage 13.8).
        var ole = OleInitialize(0) >= 0;
        try
        {
            Safe(() => Started?.Invoke(), "al arrancar");
            Loop();
        }
        catch (Exception ex)
        {
            Log.Error("Asistente: el motor se ha detenido por un error", ex);
        }
        finally
        {
            Safe(() => Stopping?.Invoke(), "al parar");
            if (_timer != 0) CloseHandle(_timer);
            _timer = 0;
            if (ole) OleUninitialize();
        }
    }

    private void Loop()
    {
        _nextTick = _lastTick = Now;
        while (_running)
        {
            var busy = Rendering.RenderStats.Now();
            PumpMessages();
            RunOrders();
            if (!_running) break;

            if (_tickSoon)
            {
                _tickSoon = false;
                _nextTick = Math.Min(_nextTick, _lastTick + MinInterval);
            }

            var now = Now;
            if (now >= _nextTick)
            {
                var interval = 1.0;
                if (Tick is { } tick)
                {
                    try { interval = tick(now); }
                    catch (Exception ex) { Log.Error("Asistente: error en el ciclo del motor", ex); }
                }
                _lastTick = now;
                interval = double.IsFinite(interval) ? Math.Clamp(interval, MinInterval, 60) : 1;
                var next = Schedule?.Invoke(now, interval) ?? now + interval;
                _nextTick = next > now ? next : now + interval;
            }

            Rendering.RenderStats.Wake(busy);
            Wait(Math.Max(_nextTick - Now, 0.0005));
        }
    }

    private unsafe void Wait(double seconds)
    {
        var due = -(long)(seconds * 10_000_000);
        SetWaitableTimer(_timer, in due, 0, 0, 0, false);
        var handles = stackalloc nint[2];
        handles[0] = _timer;
        handles[1] = _wake.SafeWaitHandle.DangerousGetHandle();
        MsgWaitForMultipleObjectsEx(2, handles, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }

    private static void PumpMessages()
    {
        while (PeekMessage(out var message, 0, 0, 0, PM_REMOVE))
        {
            TranslateMessage(in message);
            DispatchMessage(in message);
        }
    }

    private void RunOrders()
    {
        while (_orders.TryDequeue(out var order)) Safe(order, "en una orden");
    }

    private static void Safe(Action action, string where)
    {
        try { action(); }
        catch (Exception ex) { Log.Error($"Asistente: error en el motor {where}", ex); }
    }

    /// <summary>Continuations of awaits started on the engine thread go back to its queue.</summary>
    private sealed class EngineContext(EngineLoop loop) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => loop.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (loop.IsEngineThread)
            {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim();
            Exception? error = null;
            loop.Post(() =>
            {
                try { d(state); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait();
            if (error is not null) throw new InvalidOperationException("Error en el hilo del motor", error);
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
