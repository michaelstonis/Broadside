using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic shading-heavy pages for allocation tests and benchmarks (ISO 32000-2 §8.7.4.1, §8.7.4.5.3, §8.7.4.5.4).</summary>
public static class ShadingSamples
{
    private const string Function = "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0.5 0] /C1 [0 0.2 1] /N 1.5 >>";

    /// <summary>
    /// A 612 x 792 page: a grid of <paramref name="columns"/> × <paramref name="rows"/> cells cycling through every axial and radial
    /// paint the rasterizer has: <c>sh</c> of an extended axial shading in a rectangle clip, <c>sh</c> of a radial sphere with the
    /// AntiAlias hint, a fill and a stroke in a shading pattern with a Background (in pattern space, a different cell each time), a
    /// cone with a BBox through <c>sh</c>, and a DeviceCMYK radial shading with a stitching function.
    /// </summary>
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
                text.Append(Format("q 1 0 0 1 {0} {1} cm 0 0 {2} {3} re W n ", x, y, width, height));
                text.Append((cell % 6) switch
                {
                    0 => "/Ax sh ",
                    1 => "/Sp sh ",
                    2 => Format("/Pattern cs /P0 scn 5 5 {0} {1} re f ", width - 10, height - 10),
                    3 => Format("/Pattern CS /P0 SCN 6 w 5 5 m {0} {1} l S ", width - 5, height - 5),
                    4 => "/Co sh ",
                    _ => "/Ck sh ",
                });
                text.Append("Q\n");
            }
        }

        return ContentSamples.OnePage(
            Encoding.ASCII.GetBytes(text.ToString()),
            "/Shading << /Ax 5 0 R /Sp 6 0 R /Co 7 0 R /Ck 8 0 R >> /Pattern << /P0 9 0 R >>",
            $"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [10 10 90 80] /Extend [true true] {Function} >>",
            $"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [40 60 5 50 50 45] /AntiAlias true {Function} >>",
            $"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [20 50 30 80 50 10] /Extend [true true] /BBox [5 5 95 95] {Function} >>",
            "<< /ShadingType 3 /ColorSpace /DeviceCMYK /Coords [50 50 0 50 50 60] /Extend [false true] /Function << /FunctionType 3 /Domain [0 1] "
            + "/Bounds [0.5] /Encode [0 1 0 1] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 0 0 0] /N 1 >> "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0 0] /C1 [0 1 1 0] /N 1 >>] >> >>",
            $"<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 0 0] /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 300 0] /Background [0.9 0.9 0.8] {Function} >> >>");
    }

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
