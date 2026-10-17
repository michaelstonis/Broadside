using Broadside.GoldenImages;

namespace Broadside.Tools.RenderDiff;

/// <summary>A reference image lined up with Broadside's, and their golden-image comparison.</summary>
/// <param name="Reference">The reference image on the common canvas, moved by the offset, white where it does not reach.</param>
/// <param name="Broadside">Broadside's image on the common canvas, white where it does not reach.</param>
/// <param name="OffsetX">How far right the reference was moved.</param>
/// <param name="OffsetY">How far down the reference was moved.</param>
/// <param name="Comparison">The comparison, with the reference as the golden image.</param>
internal sealed record AlignedComparison(GoldenImage Reference, GoldenImage Broadside, int OffsetX, int OffsetY, GoldenComparison Comparison);

/// <summary>
/// Lines a reference render up with Broadside's: both go on a white canvas as large as the larger of the two (engines round page
/// sizes differently), then the reference is moved by every whole-pixel offset up to a limit and the offset with the fewest differing
/// pixels wins, the one nearest to none on a tie, provided it at least halves the differing pixels of the images as they lie. The
/// reference plays the golden image, so its edges get the edge tolerance.
/// </summary>
internal static class ImageAlignment
{
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    public static AlignedComparison Align(GoldenImage broadside, GoldenImage reference, int maxShift, GoldenThreshold threshold)
    {
        int width = Math.Max(broadside.Width, reference.Width);
        int height = Math.Max(broadside.Height, reference.Height);
        GoldenImage canvas = Place(broadside, 0, 0, width, height);
        GoldenImage placed = Place(reference, 0, 0, width, height);
        var unmoved = new AlignedComparison(placed, canvas, 0, 0, GoldenComparison.Compare(placed, canvas, threshold));
        AlignedComparison best = unmoved;
        foreach ((int dx, int dy) in Offsets(maxShift))
        {
            if (best.Comparison.DifferingPixels == 0)
            {
                break;
            }

            GoldenImage moved = Place(reference, dx, dy, width, height);
            GoldenComparison comparison = GoldenComparison.Compare(moved, canvas, threshold);
            if (comparison.DifferingPixels < best.Comparison.DifferingPixels)
            {
                best = new AlignedComparison(moved, canvas, dx, dy, comparison);
            }
        }

        // A move must explain the difference, not shave a border off it: images that differ everywhere lose a row and a column of
        // differing pixels to any move. It counts only when it at least halves the pixels that differ where the images lie.
        return best.Comparison.DifferingPixels * 2 <= unmoved.Comparison.DifferingPixels ? best : unmoved;
    }

    /// <summary>Every offset up to the limit but (0, 0), nearest first: the four at distance 1, then ...</summary>
    private static IEnumerable<(int Dx, int Dy)> Offsets(int maxShift)
    {
        int limit = Math.Max(0, maxShift);
        var offsets = new List<(int Dx, int Dy)>();
        for (int dy = -limit; dy <= limit; dy++)
        {
            for (int dx = -limit; dx <= limit; dx++)
            {
                if (dx != 0 || dy != 0)
                {
                    offsets.Add((dx, dy));
                }
            }
        }

        return offsets.OrderBy(static o => Math.Abs(o.Dx) + Math.Abs(o.Dy)).ThenBy(static o => o.Dy).ThenBy(static o => o.Dx);
    }

    private static GoldenImage Place(GoldenImage image, int dx, int dy, int width, int height)
    {
        if (dx == 0 && dy == 0 && image.Width == width && image.Height == height)
        {
            return image;
        }

        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sy = y - dy;
            for (int x = 0; x < width; x++)
            {
                int sx = x - dx;
                (byte r, byte g, byte b) = sx >= 0 && sx < image.Width && sy >= 0 && sy < image.Height ? image[sx, sy] : White;
                int offset = ((y * width) + x) * 4;
                rgba[offset] = r;
                rgba[offset + 1] = g;
                rgba[offset + 2] = b;
                rgba[offset + 3] = 255;
            }
        }

        return GoldenImage.FromPixels(rgba, width, height, width * 4, GoldenPixelFormat.Rgba8);
    }
}
