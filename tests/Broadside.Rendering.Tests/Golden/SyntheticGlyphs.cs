using Broadside.GoldenImages;

namespace Broadside.Rendering.Tests.Golden;

/// <summary>
/// Draws a small glyph the way a 72 DPI page shows a 10 point letter, with anti-aliased edges, independently of any rasterizer: each
/// pixel's coverage is the share of an n × n grid of sample points inside the shape. A fine grid stands in for exact area coverage,
/// a coarse one for another renderer's anti-aliasing, so the two differ only at edges and only by the error of the coarse grid.
/// </summary>
internal static class SyntheticGlyphs
{
    /// <summary>The side of the square images, in pixels.</summary>
    public const int Size = 40;

    /// <summary>
    /// Draws the glyph: an "H" (stems 1.4 px, a 0.8 px crossbar), a 0.6 px hairline and an "o" ring 1.2 px thick, offset by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) pixels, in <paramref name="ink"/> on white.
    /// </summary>
    public static GoldenImage Draw(double dx, double dy, int samples, (byte R, byte G, byte B) ink)
    {
        byte[] pixels = new byte[Size * Size * 4];
        double step = 1.0 / samples;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double px = x + ((sx + 0.5) * step) - dx;
                        double py = y + ((sy + 0.5) * step) - dy;
                        inside += Inside(px, py) ? 1 : 0;
                    }
                }

                double coverage = inside / (double)(samples * samples);
                int offset = ((y * Size) + x) * 4;
                pixels[offset] = Blend(ink.R, coverage);
                pixels[offset + 1] = Blend(ink.G, coverage);
                pixels[offset + 2] = Blend(ink.B, coverage);
                pixels[offset + 3] = 255;
            }
        }

        return GoldenImage.FromPixels(pixels, Size, Size, Size * 4, GoldenPixelFormat.Rgba8);
    }

    private static byte Blend(byte ink, double coverage) => (byte)Math.Round((ink * coverage) + (255 * (1 - coverage)));

    private static bool Inside(double x, double y)
    {
        // The H: two stems and a crossbar, 11 px tall.
        bool h = (InRange(x, 6, 7.4) || InRange(x, 13, 14.4) || (InRange(x, 6, 14.4) && InRange(y, 14.6, 15.4))) && InRange(y, 10, 21);

        // A hairline thinner than a pixel.
        bool hairline = InRange(x, 17.3, 17.9) && InRange(y, 10, 21);

        // The o: a ring of radii 4 and 2.8 around (25, 17).
        double r2 = ((x - 25) * (x - 25)) + ((y - 17) * (y - 17));
        bool ring = r2 <= 16 && r2 >= 2.8 * 2.8;

        return h || hairline || ring;
    }

    private static bool InRange(double value, double from, double to) => value >= from && value < to;
}
