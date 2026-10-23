using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Host.Input;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Tests;

/// <summary>SDK 1.8: a cursor drawn by the assistant over its hit area.</summary>
public class CursorTests
{
    private static BitmapSource Picture(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(200, 200, 120, 60)), null, new Point(size / 2.0, size / 2.0), size / 3.0, size / 4.0);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void APictureBecomesACursorWithItsHotspotInside()
    {
        nint made = 0, huge = 0;
        var thread = new Thread(() =>
        {
            made = CursorImages.Create(Picture(40), new Point(12, 30));
            huge = CursorImages.Create(Picture(400), new Point(1000, -5));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.NotEqual(0, made);
        Assert.NotEqual(0, huge);
        Assert.True(DestroyIcon(made));
        Assert.True(DestroyIcon(huge));
    }
}
