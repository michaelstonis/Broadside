using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #517: an upright image enlarged on both axes is painted a run of pixels at a time (its samples read into a run, then
/// composited together), and the result is the same, byte for byte, as the per-pixel reference: the nearest sample or the bilinear
/// blend at each pixel's centre (ISO 32000-2 §8.9.5.3), u stepped pixel by pixel, composited pixel by pixel (§11.3).
/// </summary>
public sealed class ImagePainterRunTests
{
    // Wider than ImagePainter's runs of 256 pixels, so spans cross a run boundary.
    private const int Width = 600;
    private const int Height = 48;
    private const int Samples = 41;

    private static readonly uint[] Pixels = RandomPixels(Samples * Samples, seed: 517);

    /// <summary>Device-to-image transforms that enlarge on both axes and keep v the same along a row (B = 0); some also shear (C ≠ 0).</summary>
    public static TheoryData<double, double, double, double, double, double> Transforms => new()
    {
        { 0.0683, 0, 0, 0.83, 0.3, 0.1 },
        { 0.0904, 0, 0, -0.9, 0, 47.2 },
        { -0.071, 0, 0, 0.5, 41.5, -3.5 },
        { 0.25, 0, 0, 0.4, -20, -2 },
        { 0.07, 0, 0.11, 0.6, -0.4, 1.25 },
        { 0.1, 0, 0, 0.1, 1e-9, 0.5 },
    };

    [Theory]
    [MemberData(nameof(Transforms))]
    public void Runs_paint_the_same_bytes_as_the_per_pixel_reference(double a, double b, double c, double d, double e, double f)
    {
        var inverse = new Matrix(a, b, c, d, e, f);
        foreach (bool interpolate in (bool[])[false, true])
        {
            foreach (Compositor compositor in (Compositor[])[default, new Compositor(0.6, BlendMode.Normal), new Compositor(0.8, BlendMode.Multiply)])
            {
                byte[] expected = SimdKernelAssert.RandomBytes(Width * Height * 4, seed: 5171);
                byte[] actual = (byte[])expected.Clone();
                var painter = new ImagePainter(actual, Width * 4, Pixels, Samples, Samples, inverse, interpolate, compositor: compositor);
                Span<uint> reference = MemoryMarshal.Cast<byte, uint>(expected.AsSpan());
                foreach ((int row, int x, int count, int coverage) in Spans())
                {
                    painter.Span(row, x, count, coverage);
                    Reference(reference.Slice(row * Width, Width), inverse, interpolate, compositor, row, x, count, coverage);
                }

                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void A_row_composites_as_its_pixels_do_in_every_blend_mode()
    {
        uint[] source = RandomPixels(300, seed: 5172);
        uint[] backdrop = RandomPixels(300, seed: 5173);
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            foreach (double alpha in (double[])[1, 0.6, 0])
            {
                var compositor = new Compositor(alpha, mode);
                foreach (int coverage in (int[])[0, 1, 128, 254, 255])
                {
                    uint[] expected = (uint[])backdrop.Clone();
                    for (int i = 0; i < expected.Length; i++)
                    {
                        expected[i] = compositor.Composite(source[i], expected[i], coverage);
                    }

                    uint[] actual = (uint[])backdrop.Clone();
                    compositor.CompositeRow(source, actual, coverage);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    /// <summary>The spans painted: every row split where the scan converter's edge pixels split it, the splits varying by row.</summary>
    private static IEnumerable<(int Row, int X, int Count, int Coverage)> Spans()
    {
        for (int row = 0; row < Height; row++)
        {
            int split = 1 + (row % 3);
            int coverage = row % 5 == 0 ? 128 : 255;
            yield return (row, 0, split, coverage);
            if (row % 4 == 1)
            {
                yield return (row, split, 300, 255);
                yield return (row, split + 300, Width - split - 300, coverage);
            }
            else
            {
                yield return (row, split, Width - split, coverage);
            }
        }
    }

    /// <summary>The per-pixel reference of a span: each pixel's sample at its centre, composited on its own.</summary>
    private static void Reference(Span<uint> line, in Matrix m, bool interpolate, Compositor compositor, int row, int x, int count, int coverage)
    {
        double u = (m.A * (x + 0.5)) + (m.C * (row + 0.5)) + m.E;
        double v = (m.B * (x + 0.5)) + (m.D * (row + 0.5)) + m.F;
        for (int i = 0; i < count; i++, u += m.A, v += m.B)
        {
            uint sample = interpolate
                ? Bilinear(u, v)
                : Pixels[(Clamp(Math.Floor(v)) * Samples) + Clamp(Math.Floor(u))];
            line[x + i] = compositor.Composite(sample, line[x + i], coverage);
        }
    }

    private static uint Bilinear(double u, double v)
    {
        double cu = u - 0.5;
        double cv = v - 0.5;
        double fu0 = Math.Floor(cu);
        double fv0 = Math.Floor(cv);
        double fu = Math.Clamp(cu - fu0, 0, 1);
        double fv = Math.Clamp(cv - fv0, 0, 1);
        uint p00 = Pixels[(Clamp(fv0) * Samples) + Clamp(fu0)];
        uint p10 = Pixels[(Clamp(fv0) * Samples) + Clamp(fu0 + 1)];
        uint p01 = Pixels[(Clamp(fv0 + 1) * Samples) + Clamp(fu0)];
        uint p11 = Pixels[(Clamp(fv0 + 1) * Samples) + Clamp(fu0 + 1)];
        if (p00 == p10 && p00 == p01 && p00 == p11)
        {
            return p00;
        }

        double w00 = (1 - fu) * (1 - fv);
        double w10 = fu * (1 - fv);
        double w01 = (1 - fu) * fv;
        double w11 = fu * fv;
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            double value = (w00 * ((p00 >> shift) & 0xFF)) + (w10 * ((p10 >> shift) & 0xFF)) + (w01 * ((p01 >> shift) & 0xFF)) + (w11 * ((p11 >> shift) & 0xFF));
            result |= (uint)Math.Clamp((int)(value + 0.5), 0, 255) << shift;
        }

        return result;
    }

    private static int Clamp(double value) => value >= Samples ? Samples - 1 : value > 0 ? (int)value : 0;

    private static uint[] RandomPixels(int count, int seed)
    {
        // Premultiplied: each color channel at most the alpha; runs of equal pixels, so the bilinear blend's shortcut is taken too.
        uint[] pixels = MemoryMarshal.Cast<byte, uint>(SimdKernelAssert.RandomBytes(4 * count, seed)).ToArray();
        for (int i = 0; i < pixels.Length; i++)
        {
            uint alpha = i % 7 == 0 ? 255 : pixels[i] >> 24;
            uint result = alpha << 24;
            for (int shift = 0; shift < 24; shift += 8)
            {
                result |= Math.Min((pixels[i] >> shift) & 0xFF, alpha) << shift;
            }

            pixels[i] = i % 11 == 3 ? pixels[i - 1] : result;
        }

        return pixels;
    }
}
