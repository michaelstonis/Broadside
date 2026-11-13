using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #509: a reduced image painted with its footprints' columns kept from row to row (<see cref="ImageFootprints"/>) is the same,
/// byte for byte, as one whose columns are worked out for every pixel, whatever the transform, the spans the rows arrive in, the mask
/// and the compositing; and the kept spans are found only where they start at the same pixel and u.
/// </summary>
public sealed class ImageFootprintsTests
{
    private const int Width = 160;
    private const int Height = 120;
    private const int Samples = 97;

    private static readonly uint[] Pixels = RandomPixels(Samples * Samples, seed: 509);

    private static readonly byte[] Mask = SimdKernelAssert.RandomBytes(Samples * Samples, seed: 5090);

    /// <summary>Device-to-image transforms: upright reductions (C = 0) of several ratios and offsets, flipped, skewed down (B ≠ 0), and rotated (C ≠ 0).</summary>
    public static TheoryData<double, double, double, double, double, double> Transforms => new()
    {
        { 0.61, 0, 0, 0.83, 0.3, 0.1 },
        { 1.0444665376742797, 0, 0, -1.0444665376742797, 4.4368, 120.7 },
        { 3.2, 0, 0, 2.7, -7.25, -3.5 },
        { 17.5, 0, 0, 0.4, 1, 1 },
        { -2.3, 0, 0, 1.9, 360, 2 },
        { 1.7, 0.31, 0, 1.6, 0.5, -20 },
        { 1.3, 0.2, 0.25, 1.4, -10, -5 },
    };

    [Theory]
    [MemberData(nameof(Transforms))]
    public void Kept_footprints_paint_the_same_bytes_as_footprints_worked_out_per_pixel(double a, double b, double c, double d, double e, double f)
    {
        var inverse = new Matrix(a, b, c, d, e, f);
        foreach (bool interpolate in (bool[])[false, true])
        {
            foreach (bool masked in (bool[])[false, true])
            {
                foreach (Compositor compositor in (Compositor[])[default, new Compositor(0.6, BlendMode.Multiply)])
                {
                    byte[] expected = Paint(inverse, interpolate, masked, compositor, footprints: null);
                    using var footprints = new ImageFootprints();
                    byte[] actual = Paint(inverse, interpolate, masked, compositor, footprints);
                    Assert.Equal(expected, actual);

                    // A second painter on the same footprints clears them first, so a different grid is worked out afresh.
                    Assert.Equal(Paint(new Matrix(a * 1.5, b, c, d * 1.5, e, f), interpolate, masked, compositor, null), Paint(new Matrix(a * 1.5, b, c, d * 1.5, e, f), interpolate, masked, compositor, footprints));
                }
            }
        }
    }

    [Fact]
    public void A_span_is_found_only_at_its_first_pixel_and_u_and_up_to_its_length()
    {
        using var footprints = new ImageFootprints();
        int first = footprints.Start(3, 0.25, 2);
        footprints.NextColumns[0] = 7;
        footprints.NextWeights[0] = 0.5;
        footprints.Add(1);
        footprints.Add(0);

        Assert.Equal(first, footprints.Find(3, 0.25, 2));
        Assert.Equal(first, footprints.Find(3, 0.25, 1));
        Assert.Equal(-1, footprints.Find(3, 0.25, 3));
        Assert.Equal(-1, footprints.Find(4, 0.25, 1));
        Assert.Equal(-1, footprints.Find(3, 0.25000000000000006, 1));
        Assert.Equal([7], footprints.Columns(first).ToArray());
        Assert.Equal([0.5], footprints.Weights(first).ToArray());
        Assert.True(footprints.Columns(first + 1).IsEmpty);

        footprints.Clear();
        Assert.Equal(-1, footprints.Find(3, 0.25, 1));
    }

    [Fact]
    public void Spans_past_the_most_kept_drop_the_kept_ones()
    {
        using var footprints = new ImageFootprints();
        for (int s = 0; s <= ImageFootprints.MaxSpans; s++)
        {
            footprints.Start(s, s, 1);
            footprints.Add(0);
        }

        Assert.Equal(-1, footprints.Find(0, 0, 1));
        Assert.True(footprints.Find(ImageFootprints.MaxSpans, ImageFootprints.MaxSpans, 1) >= 0);
    }

    /// <summary>
    /// Paints the 97 × 97 random image through <paramref name="inverse"/> into a 160 × 120 random backdrop, every row in spans that
    /// split at a pixel varying by row (and, on some rows, at two), as the scan converter's edge pixels split them.
    /// </summary>
    private static byte[] Paint(in Matrix inverse, bool interpolate, bool masked, Compositor compositor, ImageFootprints? footprints)
    {
        byte[] target = SimdKernelAssert.RandomBytes(Width * Height * 4, seed: 5091);
        var painter = new ImagePainter(
            target,
            Width * 4,
            Pixels,
            Samples,
            Samples,
            inverse,
            interpolate,
            masked ? Mask : null,
            Samples,
            Samples,
            maskInterpolate: false,
            compositor,
            average: true,
            footprints: footprints);
        for (int row = 0; row < Height; row++)
        {
            int split = 1 + (row % 3);
            int coverage = row % 5 == 0 ? 128 : 255;
            painter.Span(row, 0, split, coverage);
            if (row % 4 == 1)
            {
                painter.Span(row, split, 50, 255);
                painter.Span(row, split + 50, Width - split - 50, coverage);
            }
            else
            {
                painter.Span(row, split, Width - split, coverage);
            }
        }

        return target;
    }

    private static uint[] RandomPixels(int count, int seed)
    {
        // Premultiplied: each colour channel at most the alpha.
        uint[] pixels = MemoryMarshal.Cast<byte, uint>(SimdKernelAssert.RandomBytes(4 * count, seed)).ToArray();
        for (int i = 0; i < pixels.Length; i++)
        {
            uint alpha = i % 7 == 0 ? 255 : pixels[i] >> 24;
            uint result = alpha << 24;
            for (int shift = 0; shift < 24; shift += 8)
            {
                result |= Math.Min((pixels[i] >> shift) & 0xFF, alpha) << shift;
            }

            pixels[i] = result;
        }

        return pixels;
    }
}
