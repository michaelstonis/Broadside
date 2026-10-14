using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic overprint-heavy pages for allocation tests and benchmarks (ISO 32000-2 §8.6.7, §11.7.4).</summary>
public static class OverprintSamples
{
    private const int Columns = 8;

    /// <summary>
    /// A 612 x 792 page of <paramref name="cells"/> small cells, each a DeviceCMYK backdrop and an overlapping overlay under an
    /// overprinting ExtGState (OP and OPM cycling through the cells), the overlay cycling through every paint the preview handles:
    /// a DeviceCMYK fill, a Separation spot fill, a DeviceN fill (a process and a spot colourant), a stroke, a 2 x 2 DeviceCMYK
    /// inline image, a stencil mask and an axial DeviceCMYK shading in a clip. Two spot colourants in all, whatever the cell count,
    /// so the colourant planes a render needs do not grow with it.
    /// </summary>
    /// <param name="cells">The number of cells.</param>
    /// <returns>The file.</returns>
    public static byte[] Grid(int cells)
    {
        int rows = (cells + Columns - 1) / Columns;
        double width = 612.0 / Columns;
        double height = 792.0 / Math.Max(rows, 1);
        var text = new StringBuilder();
        for (int cell = 0; cell < cells; cell++)
        {
            double x = cell % Columns * width;
            double y = cell / Columns * height;
            text.Append(Format("q 1 0 0 1 {0} {1} cm 0.8 0.1 0 0 k 0 0 {2} {3} re f /G{4} gs ", x, y, width * 0.6, height * 0.6, cell % 3));
            text.Append((cell % 7) switch
            {
                0 => Format("0 0.9 0 0 k {0} {1} {0} {1} re f ", width * 0.3, height * 0.3),
                1 => Format("/Spot cs 0.7 scn {0} {1} {0} {1} re f ", width * 0.3, height * 0.3),
                2 => Format("/Duo cs 0.5 0.6 scn {0} {1} {0} {1} re f ", width * 0.3, height * 0.3),
                3 => Format("0 0 1 0 K 3 w 0 0 m {0} {1} l S ", width, height),
                4 => Format("{0} 0 0 {1} {0} {1} cm BI /W 2 /H 2 /CS /CMYK /BPC 8 ID {2} EI ", width * 0.3, height * 0.3, Image),
                5 => Format("/Spot cs 1 scn {0} 0 0 {1} {0} {1} cm BI /W 8 /H 2 /IM true ID ¥Z EI ", width * 0.3, height * 0.3),
                _ => Format("{0} {1} {0} {1} re W n {0} 0 0 {1} {0} {1} cm /Sh sh ", width * 0.3, height * 0.3),
            });
            text.Append("Q\n");
        }

        const string resources =
            "/ExtGState << /G0 << /OP true /op true /OPM 1 >> /G1 << /OP true /op true /OPM 0 >> /G2 << /OP false /op false >> >>"
            + " /ColorSpace << /Spot [/Separation /SampleSpot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0]"
            + " /C1 [0 0.5 1 0] /N 1 >>] /Duo [/DeviceN [/Cyan /SampleSpotTwo] /DeviceCMYK 5 0 R] >>"
            + " /Shading << /Sh << /ShadingType 2 /ColorSpace /DeviceCMYK /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1]"
            + " /C0 [1 0 0 0] /C1 [0 1 0 0] /N 1 >> /Extend [true true] >> >>";

        // Object 5, Duo's tint transform: (c, s) to c 0.5s s 0.
        return ContentSamples.OnePage(
            Encoding.Latin1.GetBytes(text.ToString()),
            resources,
            ContentSamples.Stream("{dup 0.5 mul exch 0}", "/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1]"));
    }

    private static string Image { get; } = new([(char)255, (char)0, (char)0, (char)0, (char)0, (char)255, (char)0, (char)0, (char)0, (char)0, (char)255, (char)0, (char)0, (char)0, (char)0, (char)255]);

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
