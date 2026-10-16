using Broadside.Graphics;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Rasterizes the triangles of a mesh shading (ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8) into device pixels, which Direct2D cannot
/// shade itself: at each pixel centre inside a triangle the corners' colour components are interpolated in the shading's colour space
/// (or t, with a function, then looked up in the function's table) and converted to sRGB, later triangles covering earlier ones.
/// Pixels outside every triangle stay transparent.
/// </summary>
/// <remarks>Runs once per mesh, transform and pixel block per backend; the image is kept with the shading.</remarks>
internal static class MeshRasterizer
{
    /// <summary>The most pixels a rasterized mesh may have; a larger block is rasterized at a coarser step and enlarged.</summary>
    public const long MaxPixels = 1L << 22;

    /// <summary>
    /// Rasterizes <paramref name="mesh"/> mapped by <paramref name="toPixels"/> over the device pixels of <paramref name="bounds"/>,
    /// one image pixel per <paramref name="step"/> × <paramref name="step"/> device pixels.
    /// </summary>
    public static PixelImage Rasterize(MeshTriangles mesh, in Matrix toPixels, PixelRect bounds, int step)
    {
        int width = (bounds.Width + step - 1) / step;
        int height = (bounds.Height + step - 1) / step;
        byte[] pixels = new byte[width * height * 4];
        int stride = mesh.Stride;
        MeshTable? table = mesh.Table;
        PdfColorConverter converter = mesh.Converter;
        ReadOnlySpan<PathPoint> points = mesh.Points;
        ReadOnlySpan<float> components = mesh.Components;
        Span<float> color = stackalloc float[Math.Max(stride, 1)];
        Span<float> rgb = stackalloc float[4];
        Span<double> x = stackalloc double[3];
        Span<double> y = stackalloc double[3];
        for (int t = 0; t < points.Length / 3; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                PathPoint p = toPixels.Transform(points[(3 * t) + k]);
                x[k] = (p.X - bounds.Left) / step;
                y[k] = (p.Y - bounds.Top) / step;
            }

            double area = ((x[1] - x[0]) * (y[2] - y[0])) - ((x[2] - x[0]) * (y[1] - y[0]));
            if (!(Math.Abs(area) > 1e-12) || !double.IsFinite(area))
            {
                continue;
            }

            int left = Math.Max(0, (int)Math.Floor(Math.Min(x[0], Math.Min(x[1], x[2]))));
            int right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(x[0], Math.Max(x[1], x[2]))));
            int top = Math.Max(0, (int)Math.Floor(Math.Min(y[0], Math.Min(y[1], y[2]))));
            int bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(y[0], Math.Max(y[1], y[2]))));
            ReadOnlySpan<float> ca = components.Slice(3 * t * stride, stride);
            ReadOnlySpan<float> cb = components.Slice(((3 * t) + 1) * stride, stride);
            ReadOnlySpan<float> cc = components.Slice(((3 * t) + 2) * stride, stride);
            for (int j = top; j <= bottom; j++)
            {
                double py = j + 0.5;
                for (int i = left; i <= right; i++)
                {
                    double px = i + 0.5;
                    double wa = (((x[1] - px) * (y[2] - py)) - ((x[2] - px) * (y[1] - py))) / area;
                    double wb = (((x[2] - px) * (y[0] - py)) - ((x[0] - px) * (y[2] - py))) / area;
                    double wc = 1 - wa - wb;
                    const double epsilon = -1e-9;
                    if (wa < epsilon || wb < epsilon || wc < epsilon)
                    {
                        continue;
                    }

                    byte r;
                    byte g;
                    byte b;
                    if (table is not null)
                    {
                        double value = (wa * ca[0]) + (wb * cb[0]) + (wc * cc[0]);
                        double s = table.T1 != table.T0 ? Math.Clamp((value - table.T0) / (table.T1 - table.T0), 0, 1) : 0;
                        (r, g, b) = Lookup(table.Colors, s);
                    }
                    else
                    {
                        for (int k = 0; k < stride; k++)
                        {
                            color[k] = (float)((wa * ca[k]) + (wb * cb[k]) + (wc * cc[k]));
                        }

                        converter.Convert(color[..stride], rgb);
                        (r, g, b) = (Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
                    }

                    int o = ((j * width) + i) * 4;
                    pixels[o] = b;
                    pixels[o + 1] = g;
                    pixels[o + 2] = r;
                    pixels[o + 3] = 255;
                }
            }
        }

        return PixelImage.Own(pixels, width, height, 4);
    }

    private static (byte R, byte G, byte B) Lookup(byte[] colors, double s)
    {
        double position = s * (ShadingBuilder.TableSize - 1);
        int i = Math.Min((int)position, ShadingBuilder.TableSize - 2);
        double f = position - i;
        return (Mix(colors[3 * i], colors[3 * (i + 1)], f), Mix(colors[(3 * i) + 1], colors[(3 * (i + 1)) + 1], f), Mix(colors[(3 * i) + 2], colors[(3 * (i + 1)) + 2], f));
    }

    private static byte Mix(byte a, byte b, double f) => (byte)((a + ((b - a) * f)) + 0.5);

    private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);
}
