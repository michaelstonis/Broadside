using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic tiling-pattern pages for allocation tests and benchmarks (ISO 32000-2 §8.7.3).</summary>
public static class TilingSamples
{
    /// <summary>
    /// A 612 x 792 page: a grid of <paramref name="columns"/> × <paramref name="rows"/> cells cycling through the tiling paints the
    /// rasterizer has: a fill with a small coloured cell (constant spacing), the same fill under alpha 0.5, a fill with an uncoloured
    /// cell in two colours, a stroke, a fill whose steps are smaller than the bounding box (overlapping cells drawn with alpha), a
    /// rotated and scaled pattern space with no distortion, and a cell that paints with another pattern.
    /// </summary>
    /// <param name="columns">The columns of the grid.</param>
    /// <param name="rows">The rows of the grid.</param>
    /// <returns>The file.</returns>
    public static byte[] Grid(int columns = 6, int rows = 8)
    {
        var text = new StringBuilder();
        double width = 612.0 / columns;
        double height = 792.0 / rows;
        int cell = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++, cell++)
            {
                double x = column * width;
                double y = row * height;
                text.Append(Format("q 1 0 0 1 {0} {1} cm ", x, y));
                text.Append((cell % 7) switch
                {
                    0 => Format("/Pattern cs /Pc scn 4 4 {0} {1} re f ", width - 8, height - 8),
                    1 => Format("/Half gs /Pattern cs /Pc scn 4 4 {0} {1} re f ", width - 8, height - 8),
                    2 => Format("/Cs1 cs 0.9 0.1 0.3 /Pu scn 4 4 {0} {1} re f 0.1 0.4 0.9 /Pu scn 10 10 20 20 re f ", width - 8, height - 8),
                    3 => Format("/Pattern CS /Pc SCN 8 w 6 6 m {0} {1} l S ", width - 6, height - 6),
                    4 => Format("/Pattern cs /Po scn 4 4 {0} {1} re f ", width - 8, height - 8),
                    5 => Format("/Pattern cs /Pr scn 4 4 {0} {1} re f ", width - 8, height - 8),
                    _ => Format("/Pattern cs /Pn scn 4 4 {0} {1} re f ", width - 8, height - 8),
                });
                text.Append("Q\n");
            }
        }

        return ContentSamples.OnePage(
            Encoding.ASCII.GetBytes(text.ToString()),
            "/ExtGState << /Half << /ca 0.5 >> >> /ColorSpace << /Cs1 [/Pattern /DeviceRGB] >> "
            + "/Pattern << /Pc 5 0 R /Pu 6 0 R /Po 7 0 R /Pr 8 0 R /Pn 9 0 R >>",
            Pattern("1 0 0 rg 0 0 3 3 re f 0 0 1 rg 3 3 3 3 re f 0 g 1 4 m 2 5 l S", "/PaintType 1 /TilingType 1 /BBox [0 0 6 6] /XStep 6.5 /YStep 6.5"),
            Pattern("0 0 m 5 0 l 2.5 5 l f", "/PaintType 2 /TilingType 2 /BBox [0 0 5 5] /XStep 7 /YStep 7"),
            Pattern("/A gs 0 0.5 0 rg 0 0 12 12 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 12 12] /XStep 5 /YStep 5", "/ExtGState << /A << /ca 0.4 >> >>"),
            Pattern("0.6 0.2 0.8 rg 0 0 4 2 re f", "/PaintType 1 /TilingType 2 /BBox [0 0 5 5] /XStep 5 /YStep 5 /Matrix [1.2 0.7 -0.7 1.2 3 1]"),
            Pattern("/Pattern cs /Pc scn 0 0 8 8 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 12 12] /XStep 12 /YStep 12", "/Pattern << /Pc 5 0 R >>"));
    }

    private static string Pattern(string content, string entries, string resources = "") =>
        ContentSamples.Stream(content, $"/Type /Pattern /PatternType 1 /Resources << {resources} >> {entries}");

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
