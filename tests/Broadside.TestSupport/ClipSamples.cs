using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>What <see cref="ClipSamples.Grid"/> clips each cell's paints with.</summary>
public enum ClipSampleKind
{
    /// <summary>No clip: the same paints unclipped, the baseline.</summary>
    None,

    /// <summary>A rectangle (<c>re W n</c>) and, inside a nested <c>q</c>, a smaller one: the fast path with no mask.</summary>
    Rectangles,

    /// <summary>A triangle (<c>W n</c>) and, inside a nested <c>q</c>, a diamond with <c>W*</c>: coverage masks, one copied per level.</summary>
    Paths,
}

/// <summary>Synthetic clip-heavy content streams for allocation tests and benchmarks (ISO 32000-2 §8.5.4).</summary>
public static class ClipSamples
{
    /// <summary>
    /// A content stream for a 612 x 792 page: a grid of <paramref name="columns"/> × <paramref name="rows"/> cells, each painting
    /// two fills inside <c>q</c>/<c>Q</c> with the clips of <paramref name="kind"/>, so every kind paints the same fills.
    /// </summary>
    public static byte[] Grid(ClipSampleKind kind, int columns = 12, int rows = 16)
    {
        var text = new StringBuilder();
        double width = 612.0 / columns;
        double height = 792.0 / rows;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double x = column * width;
                double y = row * height;
                text.Append("q ");
                text.Append(kind switch
                {
                    ClipSampleKind.Rectangles => Format("{0} {1} {2} {3} re W n ", x + 2.25, y + 2.25, width - 4.5, height - 4.5),
                    ClipSampleKind.Paths => Format("{0} {1} m {2} {1} l {3} {4} l h W n ", x, y, x + width, x + (width / 2), y + height),
                    _ => string.Empty,
                });
                text.Append(Format("0.5 g {0} {1} {2} {3} re f q ", x, y, width, height));
                text.Append(kind switch
                {
                    ClipSampleKind.Rectangles => Format("{0} {1} {2} {3} re W n ", x + 10.5, y + 10.5, width - 21, height - 21),
                    ClipSampleKind.Paths => Format(
                        "{0} {1} m {2} {3} l {0} {4} l {5} {3} l h W* n ",
                        x + (width / 2), y + 4, x + width - 4, y + (height / 2), y + height - 4, x + 4),
                    _ => string.Empty,
                });
                text.Append(Format("1 0 0 rg {0} {1} {2} {3} re f Q Q\n", x, y, width, height));
            }
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
