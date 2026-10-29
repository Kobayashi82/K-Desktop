using KDesktop.Core.Platform;

namespace KDesktop.Core.Imaging;

/// <summary>What the capture editor (F1) does to the pixels themselves.</summary>
public static class ImageEffects
{
    /// <summary>
    /// «Difuminar»: the rectangle becomes blocks of <paramref name="block"/> pixels averaged, smoothed between their
    /// centres, so it looks blurred and what was there (a password, a name) cannot be read back. Outside the picture is
    /// left out.
    /// </summary>
    public static void Blur(BgraImage image, int x, int y, int width, int height, int block = 12)
    {
        var left = Math.Clamp(x, 0, image.Width);
        var top = Math.Clamp(y, 0, image.Height);
        var right = Math.Clamp(x + width, 0, image.Width);
        var bottom = Math.Clamp(y + height, 0, image.Height);
        var w = right - left;
        var h = bottom - top;
        if (w <= 0 || h <= 0) return;
        block = Math.Max(2, block);

        // The average colour of every block.
        var cols = (w + block - 1) / block;
        var rows = (h + block - 1) / block;
        var grid = new float[cols * rows * 3];
        var px = image.Pixels;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                double b = 0, g = 0, red = 0;
                var count = 0;
                for (var yy = top + r * block; yy < Math.Min(bottom, top + (r + 1) * block); yy++)
                    for (var xx = left + c * block; xx < Math.Min(right, left + (c + 1) * block); xx++)
                    {
                        var i = yy * image.Stride + xx * 4;
                        b += px[i];
                        g += px[i + 1];
                        red += px[i + 2];
                        count++;
                    }
                var k = (r * cols + c) * 3;
                grid[k] = (float)(b / count);
                grid[k + 1] = (float)(g / count);
                grid[k + 2] = (float)(red / count);
            }

        // Each pixel from the four block centres around it.
        for (var yy = top; yy < bottom; yy++)
        {
            var gy = Math.Clamp((yy - top + 0.5f) / block - 0.5f, 0, rows - 1);
            var r0 = (int)gy;
            var r1 = Math.Min(r0 + 1, rows - 1);
            var fy = gy - r0;
            for (var xx = left; xx < right; xx++)
            {
                var gx = Math.Clamp((xx - left + 0.5f) / block - 0.5f, 0, cols - 1);
                var c0 = (int)gx;
                var c1 = Math.Min(c0 + 1, cols - 1);
                var fx = gx - c0;
                var i = yy * image.Stride + xx * 4;
                for (var ch = 0; ch < 3; ch++)
                {
                    var a = grid[(r0 * cols + c0) * 3 + ch];
                    var bb = grid[(r0 * cols + c1) * 3 + ch];
                    var cc = grid[(r1 * cols + c0) * 3 + ch];
                    var d = grid[(r1 * cols + c1) * 3 + ch];
                    var v = (a * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy;
                    px[i + ch] = (byte)Math.Clamp(v + 0.5f, 0, 255);
                }
            }
        }
    }
}
