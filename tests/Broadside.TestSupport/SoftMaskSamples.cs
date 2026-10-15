using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic soft-mask pages for allocation tests and benchmarks (ISO 32000-2 §11.5, §11.6.4.3, §11.6.5.1).</summary>
public static class SoftMaskSamples
{
    // Object 5: a luminosity mask's group (a gray axial shading); 6: an alpha mask's group; 7: an inverting transfer function;
    // 8: a transparency group of two squares; 9: a knockout group painting under a shape mask; 10: the shading.
    private const string States =
        "/ExtGState << /L << /SMask << /S /Luminosity /G 5 0 R /BC [0.2] >> >> /A << /SMask << /S /Alpha /G 6 0 R /TR 7 0 R >> >>"
        + " /S << /SMask << /S /Luminosity /G 5 0 R >> /AIS true >> /N << /SMask /None >> /Half << /ca 0.5 >> >>"
        + " /XObject << /G0 8 0 R /K0 9 0 R >> /Shading << /Sh 10 0 R >>";

    /// <summary>
    /// A 612 x 792 page of a 3 x 4 grid of cells, each painting <paramref name="paints"/> squares through a soft mask another way: a
    /// luminosity mask with a backdrop colour, an alpha mask with a transfer function, a mask set under a rotated transform, a
    /// shape mask, a group painted under a mask, a knockout group holding a masked paint, masking turned off again, and an image
    /// and a shading under a mask. Every paint of a cell shares its cell's mask.
    /// </summary>
    /// <param name="paints">The squares each cell paints.</param>
    /// <returns>The file.</returns>
    public static byte[] Grid(int paints = 6)
    {
        const double width = 204;
        const double height = 198;
        string[] cells =
        [
            "/L gs",
            "/A gs",
            "100 100 cm 0.8 0.6 -0.6 0.8 0 0 cm -100 -100 cm /L gs 100 100 cm 0.8 -0.6 0.6 0.8 0 0 cm -100 -100 cm",
            "/S gs",
            "/L gs /G0 Do",
            "/L gs /K0 Do",
            "/A gs /N gs",
            "/L gs q 160 0 0 120 20 20 cm BI /W 2 /H 2 /CS /G /BPC 8 /F /AHx ID 2080C0FF> EI Q",
            "/A gs /Sh sh",
            "/Half gs /L gs",
            "/S gs /Half gs",
            "q /L gs Q /A gs",
        ];

        var content = new StringBuilder();
        for (int index = 0; index < cells.Length; index++)
        {
            double x = index % 3 * width;
            double y = index / 3 * height;
            content.Append(CultureInfo.InvariantCulture, $"q 1 0 0 1 {x} {y} cm 0 0 {width} {height} re W n {cells[index]} ");
            for (int paint = 0; paint < paints; paint++)
            {
                double offset = 10 + (paint * 120.0 / Math.Max(paints, 1));
                content.Append(CultureInfo.InvariantCulture, $"{(paint % 3) / 2.0} 0.3 {1 - ((paint % 3) / 2.0)} rg {offset} {offset} 60 50 re f ");
            }

            content.Append("Q ");
        }

        return ContentSamples.OnePage(
            Encoding.Latin1.GetBytes(content.ToString()),
            States,
            Form("/Group << /S /Transparency /CS /DeviceGray >> /BBox [0 0 200 200]", "/Sh sh"),
            Form("/Group << /S /Transparency >> /BBox [0 0 200 200]", "/Half gs 20 20 120 120 re f 60 60 120 120 re f"),
            "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>",
            Form("/Group << /S /Transparency >> /BBox [0 0 200 200]", "1 0 0 rg 30 30 100 100 re f 80 80 100 100 re f"),
            Form("/Group << /S /Transparency /K true >> /BBox [0 0 200 200]", "1 0 0 rg 30 30 140 140 re f /S gs 0 0 1 rg 50 50 100 100 re f"),
            "<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 200] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> /Extend [true true] >>");
    }

    private static string Form(string entries, string content) =>
        ContentSamples.Stream(content, $"/Type /XObject /Subtype /Form {entries} /Resources << {States} >>");
}
