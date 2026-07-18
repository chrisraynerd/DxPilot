using System.Drawing;

namespace JtdxAutoResume.V3.Services;

public sealed class PixelDetector
{
    public (int greyPercent, int redPercent) GetEnableTxStats(
        int centerX,
        int centerY,
        int radius,
        int greyRgb,
        int redRgb,
        int tolerance)
    {
        return PixelStats(centerX - radius, centerY - radius, centerX + radius, centerY + radius, greyRgb, redRgb, tolerance);
    }

    public int PixelPercentMatch(int x, int y, int radius, int targetRgb, int tolerance)
    {
        var (x1, y1, x2, y2) = (x - radius, y - radius, x + radius, y + radius);
        var targetR = (targetRgb >> 16) & 0xFF;
        var targetG = (targetRgb >> 8) & 0xFF;
        var targetB = targetRgb & 0xFF;

        var width = Math.Max(1, x2 - x1 + 1);
        var height = Math.Max(1, y2 - y1 + 1);
        var total = width * height;
        var match = 0;

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(x1, y1, 0, 0, bitmap.Size);

        for (var yy = 0; yy < height; yy++)
        for (var xx = 0; xx < width; xx++)
        {
            var color = bitmap.GetPixel(xx, yy);
            if (Within(color, targetR, targetG, targetB, tolerance))
                match++;
        }

        return (int)Math.Round(100.0 * match / Math.Max(1, total));
    }

    public int GetScreenRgb(int x, int y)
    {
        using var bitmap = new Bitmap(1, 1);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1));

        return bitmap.GetPixel(0, 0).ToArgb() & 0xFFFFFF;
    }

    public static bool TryParseRgb(string value, out int rgb)
    {
        rgb = 0;
        value = value.Trim().ToUpperInvariant().Replace("#", "").Replace("0X", "").Trim();
        return value.Length == 6
            && int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out rgb);
    }

    private static (int greyPercent, int redPercent) PixelStats(
        int x1,
        int y1,
        int x2,
        int y2,
        int greyRgb,
        int redRgb,
        int tolerance)
    {
        var greyR = (greyRgb >> 16) & 0xFF;
        var greyG = (greyRgb >> 8) & 0xFF;
        var greyB = greyRgb & 0xFF;
        var redR = (redRgb >> 16) & 0xFF;
        var redG = (redRgb >> 8) & 0xFF;
        var redB = redRgb & 0xFF;

        var width = Math.Max(1, x2 - x1 + 1);
        var height = Math.Max(1, y2 - y1 + 1);
        var total = width * height;
        var grey = 0;
        var red = 0;

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(x1, y1, 0, 0, bitmap.Size);

        for (var yy = 0; yy < height; yy++)
        for (var xx = 0; xx < width; xx++)
        {
            var color = bitmap.GetPixel(xx, yy);
            if (Within(color, greyR, greyG, greyB, tolerance))
                grey++;
            if (Within(color, redR, redG, redB, tolerance))
                red++;
        }

        return (
            (int)Math.Round(100.0 * grey / Math.Max(1, total)),
            (int)Math.Round(100.0 * red / Math.Max(1, total)));
    }

    private static bool Within(Color color, int r, int g, int b, int tolerance)
    {
        return Math.Abs(color.R - r) <= tolerance
            && Math.Abs(color.G - g) <= tolerance
            && Math.Abs(color.B - b) <= tolerance;
    }
}
