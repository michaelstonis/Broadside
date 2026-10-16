using Broadside.Graphics;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Evaluates a radial shading whose circles are not nested (ISO 32000-2 §8.7.4.5.4: cones, and circles of equal radius), which
/// Direct2D's focal radial brush cannot draw, at each device pixel centre: s is the largest root of |p − c(s)| = r(s) with r(s) ≥ 0
/// that the Extend flags allow, the colour the gradient's table at s. Pixels on no circle stay transparent.
/// </summary>
/// <remarks>Runs once per shading, transform and pixel block per backend; the image is kept with the shading.</remarks>
internal static class GradientRasterizer
{
    /// <summary>Evaluates <paramref name="gradient"/> mapped by <paramref name="toPixels"/> over <paramref name="bounds"/>, one image pixel per <paramref name="step"/>² device pixels.</summary>
    public static PixelImage? Rasterize(GradientShape gradient, in Matrix toPixels, PixelRect bounds, int step)
    {
        if (!toPixels.TryInvert(out Matrix back))
        {
            return null;
        }

        int width = (bounds.Width + step - 1) / step;
        int height = (bounds.Height + step - 1) / step;
        byte[] pixels = new byte[width * height * 4];
        byte[] colors = gradient.Colors;
        double cx = gradient.End.X - gradient.Start.X;
        double cy = gradient.End.Y - gradient.Start.Y;
        double r0 = gradient.StartRadius;
        double dr = gradient.EndRadius - r0;
        double a = (cx * cx) + (cy * cy) - (dr * dr);
        for (int row = 0; row < height; row++)
        {
            double y = bounds.Top + ((row + 0.5) * step);
            for (int column = 0; column < width; column++)
            {
                double x = bounds.Left + ((column + 0.5) * step);
                PathPoint p = back.Transform(x, y);
                double px = p.X - gradient.Start.X;
                double py = p.Y - gradient.Start.Y;
                double b = (px * cx) + (py * cy) + (r0 * dr);
                double c = (px * px) + (py * py) - (r0 * r0);
                if (!TrySolve(gradient, a, b, c, r0, dr, out double s))
                {
                    continue;
                }

                double position = Math.Clamp(s, 0, 1) * (ShadingBuilder.Stops - 1);
                int i = Math.Min((int)position, ShadingBuilder.Stops - 2);
                double f = position - i;
                int o = ((row * width) + column) * 4;
                for (int k = 0; k < 3; k++)
                {
                    double value = colors[(3 * i) + k] + ((colors[(3 * (i + 1)) + k] - colors[(3 * i) + k]) * f);
                    pixels[o + 2 - k] = (byte)(value + 0.5);
                }

                pixels[o + 3] = 255;
            }
        }

        return PixelImage.Own(pixels, width, height, 4);
    }

    /// <summary>Finds the largest s with r(s) ≥ 0 on whose circle the point lies (a s² − 2 b s + c = 0) and that Extend allows.</summary>
    private static bool TrySolve(GradientShape gradient, double a, double b, double c, double r0, double dr, out double s)
    {
        if (Math.Abs(a) < 1e-12)
        {
            s = c / (2 * b);
            return b != 0 && Allowed(gradient, s, r0, dr);
        }

        double discriminant = (b * b) - (a * c);
        if (!(discriminant >= 0))
        {
            s = 0;
            return false;
        }

        double root = Math.Sqrt(discriminant);
        double s1 = (b + root) / a;
        double s2 = (b - root) / a;
        (double high, double low) = s1 >= s2 ? (s1, s2) : (s2, s1);
        if (Allowed(gradient, high, r0, dr))
        {
            s = high;
            return true;
        }

        s = low;
        return Allowed(gradient, low, r0, dr);
    }

    private static bool Allowed(GradientShape gradient, double s, double r0, double dr) =>
        double.IsFinite(s) && r0 + (s * dr) >= 0 && (s >= 0 || gradient.ExtendStart) && (s <= 1 || gradient.ExtendEnd);
}
