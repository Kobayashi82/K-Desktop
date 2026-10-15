using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Web;
using QRCoder;

namespace KDesktop.Views.Dialogs;

/// <summary>Shows a one-time pairing code as a QR (with the app's address) and as text; closes when a device uses it.</summary>
public partial class WebPairDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly WebDevices _devices;
    private readonly string _baseUrl;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private WebPairingCode? _code;

    /// <summary>The device that paired while the dialog was open.</summary>
    public WebDevice? PairedDevice { get; private set; }

    private WebPairDialog(WebDevices devices, string baseUrl)
    {
        InitializeComponent();
        _devices = devices;
        _baseUrl = baseUrl;
        UrlText.Text = baseUrl;
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            WarnText.Text = Loc.T("web.pairNoHttps");
            WarnText.Visibility = Visibility.Visible;
        }

        _timer.Tick += (_, _) => Tick();
        _devices.Paired += OnPaired;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _devices.Paired -= OnPaired;
            if (PairedDevice is null) _devices.CancelCode();
        };
        NewCode();
    }

    public static WebDevice? Show(Window? owner, WebDevices devices, string baseUrl)
    {
        var dlg = new WebPairDialog(devices, baseUrl) { Owner = owner };
        dlg.ShowDialog();
        return dlg.PairedDevice;
    }

    private void NewCode()
    {
        _code = _devices.NewCode();
        CodeText.Text = $"{_code.Code[..4]}-{_code.Code[4..]}";
        QrImage.Source = RenderQr($"{_baseUrl}#pair={_code.Code}");
        _timer.Start();
        Tick();

        // A small "pop" so a new code is noticed.
        var pop = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        QrScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        QrScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        QrCard.Opacity = 1;
    }

    private void Tick()
    {
        if (_code is null) return;
        var left = _code.Expires - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            _timer.Stop();
            TimeBar.Value = 0;
            TimeText.Text = Loc.T("web.codeExpired");
            QrCard.Opacity = 0.25;
            return;
        }
        TimeBar.Value = left / WebDevices.CodeLifetime;
        TimeText.Text = Loc.T("web.codeExpires", $"{(int)left.TotalMinutes}:{left.Seconds:00}");
    }

    private void OnPaired(WebDevice device) => Dispatcher.BeginInvoke(() =>
    {
        PairedDevice = device;
        Close();
    });

    private void OnNewCode(object sender, RoutedEventArgs e) => NewCode();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>The QR as crisp vector modules, black on white (phones read it best that way, in any theme).</summary>
    private static DrawingImage RenderQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var y = 0; y < matrix.Count; y++)
            for (var x = 0; x < matrix[y].Count; x++)
                if (matrix[y][x]) geometry.Children.Add(new RectangleGeometry(new Rect(x, y, 1.02, 1.02)));
        geometry.Freeze();
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, matrix.Count, matrix.Count))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
