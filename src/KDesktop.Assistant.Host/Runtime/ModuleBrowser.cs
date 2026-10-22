using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The assistants' own browser (stage 8.4): a WebView2 in a window nobody sees (off the screen, a tool window: neither
/// the taskbar nor the assistants' world count it), with a session of its own in <c>Assistant\Browser</c> (its cookies,
/// never the user's), muted, without pop-ups or downloads. Opened on the first page asked for, on a thread of its own,
/// and closed after a few minutes without use (its processes take memory). One page at a time.
/// </summary>
internal sealed class ModuleBrowser(string folder) : IWebBrowser, IDisposable
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Load = TimeSpan.FromSeconds(15);

    /// <summary>What a page is once drawn: its title, address, visible text and HTML (up to 2 MB).</summary>
    private const string Extract =
        "({t: document.title, u: location.href, x: document.body ? document.body.innerText : '', h: (document.documentElement.outerHTML || '').slice(0, 2000000)})";

    private readonly SemaphoreSlim _one = new(1, 1);
    private Dispatcher? _dispatcher;
    private Window? _window;
    private WebView2? _view;
    private DispatcherTimer? _idle;
    private bool _broken;

    public async Task<BrowsedPage?> ReadAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (_broken) return null;
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dispatcher = Thread();
            var page = await dispatcher.InvokeAsync(() => LoadAsync(uri, cancellationToken)).Task.Unwrap()
                .WaitAsync(Load + TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return page;
        }
        catch (Exception ex) when (ex is TimeoutException or COMException or InvalidOperationException or WebView2RuntimeNotFoundException or JsonException)
        {
            Log.Warn($"Asistente: el navegador interno no ha podido abrir {uri.Host}: {ex.Message}");
            if (ex is WebView2RuntimeNotFoundException) _broken = true;
            return null;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>The browser's thread (WPF needs one apartment and a dispatcher for it), started the first time.</summary>
    private Dispatcher Thread()
    {
        if (_dispatcher is { } running) return running;
        var ready = new ManualResetEventSlim();
        var thread = new System.Threading.Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Asistente: navegador" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return _dispatcher!;
    }

    private async Task<BrowsedPage?> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        _idle?.Stop();
        try
        {
            var core = await OpenAsync().ConfigureAwait(true);
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
            core.NavigationCompleted += Completed;
            try
            {
                core.Navigate(uri.AbsoluteUri);
                if (!await loaded.Task.WaitAsync(Load, cancellationToken).ConfigureAwait(true)) return null;
            }
            finally
            {
                core.NavigationCompleted -= Completed;
            }
            // Its scripts draw it: until its text stops growing (checked every 0.4 s), 4 s at most.
            var json = string.Empty;
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(400, cancellationToken).ConfigureAwait(true);
                var now = await core.ExecuteScriptAsync(Extract).ConfigureAwait(true);
                var settled = i > 0 && now.Length == json.Length;
                json = now;
                if (settled) break;
            }
            using var page = JsonDocument.Parse(json);
            var root = page.RootElement;
            string Get(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
            return new BrowsedPage(Get("t"), Get("u"), Get("x"), Get("h"));
        }
        finally
        {
            _idle ??= new DispatcherTimer(Idle, DispatcherPriority.Background, (_, _) => Close(), Dispatcher.CurrentDispatcher);
            _idle.Start();
        }
    }

    private async Task<CoreWebView2> OpenAsync()
    {
        if (_view?.CoreWebView2 is { } open) return open;
        Directory.CreateDirectory(folder);
        _window = new Window
        {
            Title = "K-Desktop · navegador del asistente",
            Width = 1280,
            Height = 900,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
        };
        _view = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = folder } };
        _window.Content = _view;
        _window.Show();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await _view.EnsureCoreWebView2Async().ConfigureAwait(true);
        var core = _view.CoreWebView2;
        core.IsMuted = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        Log.Info($"Asistente: navegador interno abierto en {watch.ElapsedMilliseconds} ms (WebView2 {core.Environment.BrowserVersionString})");
        return core;
    }

    /// <summary>Unused for a while: its processes go (it opens again with the next page).</summary>
    private void Close()
    {
        _idle?.Stop();
        if (_view is null) return;
        _view.Dispose();
        _window?.Close();
        _view = null;
        _window = null;
        Log.Info("Asistente: navegador interno cerrado (sin usar)");
    }

    public void Dispose()
    {
        if (_dispatcher is not { } dispatcher) return;
        try
        {
            dispatcher.Invoke(Close, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or TaskCanceledException)
        {
            // closing anyway
        }
        dispatcher.InvokeShutdown();
        _one.Dispose();
    }
}
