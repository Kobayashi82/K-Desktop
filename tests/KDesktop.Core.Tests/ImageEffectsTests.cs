using KDesktop.Core.Imaging;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>F1: «Difuminar» in the capture editor.</summary>
public class ImageEffectsTests
{
    /// <summary>A chessboard of single pixels (black and white): the finest detail there is, like small text.</summary>
    private static BgraImage Chessboard(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var v = (byte)((x + y) % 2 == 0 ? 0 : 255);
                var i = (y * width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
        return new BgraImage(width, height, pixels);
    }

    private static byte Blue(BgraImage image, int x, int y) => image.Pixels[y * image.Stride + x * 4];

    [Fact]
    public void TheDetailInsideIsGoneAndOutsideNothingChanges()
    {
        var image = Chessboard(60, 40);
        ImageEffects.Blur(image, 10, 10, 30, 20, block: 6);

        // Inside, every pixel is close to the average grey: the pattern can't be read back.
        for (var y = 10; y < 30; y++)
            for (var x = 10; x < 40; x++)
                Assert.InRange(Blue(image, x, y), 100, 155);
        // Outside, untouched (and the alpha too).
        Assert.Equal(0, Blue(image, 0, 0));
        Assert.Equal(255, Blue(image, 1, 0));
        Assert.Equal(0, Blue(image, 40, 10));
        Assert.Equal(255, Blue(image, 9, 10));
        Assert.All(Enumerable.Range(0, 60 * 40), i => Assert.Equal(255, image.Pixels[i * 4 + 3]));
    }

    [Fact]
    public void ARectangleHalfOutsideThePictureOnlyBlursThePartInside()
    {
        var image = Chessboard(20, 20);
        ImageEffects.Blur(image, -10, -10, 20, 20, block: 4);
        Assert.InRange(Blue(image, 2, 2), 90, 165);
        Assert.Equal(0, Blue(image, 10, 10));
        ImageEffects.Blur(image, 30, 30, 10, 10);
    }
}
