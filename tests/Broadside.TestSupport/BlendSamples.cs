using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic transparency-heavy pages for allocation tests and benchmarks (ISO 32000-2 §11.3.5, §11.6.3, §11.6.4.4).</summary>
public static class BlendSamples
{
    private static readonly string[] Modes =
    [
        "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight",
        "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity",
    ];

    /// <summary>
    /// A 612 x 792 page: an opaque axial shading over the whole page as the backdrop, then a grid of 4 × 8 cells, one per blend mode
    /// and alpha (each of the 16 modes at alpha 1 and 0.5), whose paint cycles through a filled rectangle, a stroked diagonal, a
    /// 4 × 4 DeviceRGB inline image, a stencil mask and an axial shading in a rectangle clip, so every paint kind composites through
    /// every mode.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] Grid()
    {
        const int columns = 4;
        const int rows = 8;
        const double width = 612.0 / columns;
        const double height = 792.0 / rows;
        var text = new StringBuilder("/Bg sh\n");
        string image = new(Enumerable.Range(0, 48).Select(i => (char)(i * 5)).ToArray());
        for (int cell = 0; cell < columns * rows; cell++)
        {
            double x = cell % columns * width;
            double y = cell / columns * height;
            text.Append(Format("q /G{0} gs 1 0 0 1 {1} {2} cm 0.9 0.3 0.6 rg 0.1 0.8 0.4 RG ", cell, x, y));
            text.Append((cell % 5) switch
            {
                0 => Format("5 5 {0} {1} re f ", width - 10, height - 10),
                1 => Format("8 w 5 5 m {0} {1} l S ", width - 5, height - 5),
                2 => Format("{0} 0 0 {1} 5 5 cm BI /W 4 /H 4 /CS /RGB /BPC 8 ID {2} EI ", width - 10, height - 10, image),
                3 => Format("{0} 0 0 {1} 5 5 cm BI /W 8 /H 2 /IM true ID ¥Z EI ", width - 10, height - 10),
                _ => Format("5 5 {0} {1} re W n /Sh sh ", width - 10, height - 10),
            });
            text.Append("Q\n");
        }

        var states = new StringBuilder("/ExtGState <<");
        for (int cell = 0; cell < columns * rows; cell++)
        {
            string alpha = cell % 2 == 0 ? "1" : "0.5";
            states.Append(Format(" /G{0} << /BM /{1} /ca {2} /CA {2} >>", cell, Modes[cell / 2], alpha));
        }

        states.Append(" >> /Shading << /Bg 5 0 R /Sh 6 0 R >>");
        return ContentSamples.OnePage(
            Encoding.Latin1.GetBytes(text.ToString()),
            states.ToString(),
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 612 792] /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0.9 0.1] /C1 [0.1 0.2 0.6] /N 1 >> >>",
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 150 0] /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0.5 1] /C1 [1 0.5 0] /N 1 >> >>");
    }

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
