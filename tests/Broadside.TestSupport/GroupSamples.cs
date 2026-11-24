using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic transparency-group pages for allocation tests and benchmarks (ISO 32000-2 §11.4, §11.6.6, §11.7.4.4).</summary>
public static class GroupSamples
{
    private const string States =
        "/ExtGState << /Half << /ca 0.5 >> /HalfStroke << /CA 0.5 >> /Mul << /BM /Multiply >> >>";

    /// <summary>
    /// A 612 x 792 page: an opaque axial shading as the backdrop, then a 3 x 4 grid of cells, each painting through another way the
    /// rasterizer composites a group: a plain non-isolated group (painted straight into the backdrop), an isolated group of
    /// Multiply paints, isolated and non-isolated knockout groups, a non-isolated group of Multiply paints itself painted in
    /// Multiply (its alpha kept), a DeviceCMYK and a DeviceGray group holding a fill, a stroke, an inline image, a stencil mask and a
    /// shading, groups nested eight deep at alpha 0.5 beside a tiling pattern whose cell blends (a non-isolated group, §11.6.7),
    /// a translucent fill and stroke (the implicit knockout group), groups inside a
    /// knockout group, and a group under a clip.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] Grid()
    {
        const double width = 204;
        const double height = 198;
        string[] cells =
        [
            "/F0 Do",
            "/F1 Do",
            "/F2 Do",
            "/F3 Do",
            "/Mul gs /F4 Do",
            "/Half gs /F5 Do",
            "/Half gs /F6 Do",
            "/Half gs /N7 Do /Pattern cs /TP scn 20 20 160 100 re f",
            "/HalfStroke gs 12 w 1 0 0 rg 0 0 1 RG 30 30 140 120 re b /Half gs 6 w 50 50 100 80 re B",
            "/F8 Do",
            "q 40 40 120 100 re W n /Half gs /F1 Do Q",
            "/Half gs /F9 Do",
        ];
        var text = new StringBuilder("/Bg sh\n");
        for (int cell = 0; cell < cells.Length; cell++)
        {
            double x = cell % 3 * width;
            double y = cell / 3 * height;
            text.Append(Format("q 1 0 0 1 {0} {1} cm {2} Q\n", x, y, cells[cell]));
        }

        const string squares = "/Half gs 0.9 0.2 0.1 rg 20 20 100 100 re f 0.1 0.3 0.9 rg 80 60 100 100 re f";
        const string blends = "/Mul gs 0.9 0.2 0.1 rg 20 20 100 100 re f 0.1 0.9 0.3 rg 80 60 100 100 re f";
        const string mixed = "1 0 0 0 k 20 20 80 80 re f /Mul gs 0 1 0 0.2 k 60 40 80 80 re f 0 0 1 0 K 8 w 30 120 m 180 120 l S "
            + "q 60 0 0 40 120 20 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID ÿ\u0000\u0000\u0000ÿ\u0000\u0000\u0000ÿÿÿ\u0000 EI Q "
            + "q 40 0 0 40 20 140 cm BI /W 8 /H 2 /IM true ID ¥Z EI Q q 140 140 50 40 re W n /Sh sh Q";
        const string nested = "0.2 0.2 0.2 rg 40 40 120 120 re f";
        var objects = new List<string>
        {
            Shading("[0 0 612 792]", "[1 0.9 0.1]", "[0.1 0.2 0.6]"),
            Shading("[140 0 190 0]", "[0 0.5 1]", "[1 0.5 0]"),
            Form(string.Empty, squares),
            Form("/I true", blends),
            Form("/I true /K true", squares),
            Form("/K true", squares),
            Form(string.Empty, blends),
            Form("/I true /CS /DeviceCMYK", mixed),
            Form("/I true /CS /DeviceGray", mixed),
            Form("/K true", "/Half gs /F1 Do /F0 Do"),
            Form(string.Empty, "/Mul gs /F3 Do"),
        };

        // N0 holds the square; N1 to N7 each hold the one before at alpha 0.5.
        for (int level = 0; level < 8; level++)
        {
            objects.Add(Form(string.Empty, level == 0 ? nested : Format("/Half gs /N{0} Do", level - 1)));
        }

        var xobjects = new StringBuilder();
        int number = 7;
        foreach (string name in new[] { "F0", "F1", "F2", "F3", "F4", "F5", "F6", "F8", "F9" })
        {
            xobjects.Append(Format(" /{0} {1} 0 R", name, number++));
        }

        for (int level = 0; level < 8; level++)
        {
            xobjects.Append(Format(" /N{0} {1} 0 R", level, number++));
        }

        objects.Add(ContentSamples.Stream(
            "/Mul gs 0.9 0.2 0.1 rg 0 0 8 8 re f",
            "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << " + States + " >>"));
        string resources = States + " /Shading << /Bg 5 0 R /Sh 6 0 R >> /Pattern << /TP " + number + " 0 R >> /XObject <<" + xobjects + " >>";
        for (int index = 2; index < objects.Count; index++)
        {
            objects[index] = objects[index].Replace("RESOURCES", resources, StringComparison.Ordinal);
        }

        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(text.ToString()), resources, [.. objects]);
    }

    /// <summary>
    /// A 612 x 792 page in a DeviceCMYK page group, shaped like the Ghent Workgroup's blend-mode patch (GWG 16.0): flat CMYK
    /// backdrop bands, then a 4 x 4 grid of non-isolated DeviceCMYK form groups, each filling one cross shape in Normal, painted
    /// with one of the 16 blend modes (§11.3.5). Each group's result is blended onto the page group, in both of its passes.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] BlendModesInCmykPage()
    {
        string[] modes =
        [
            "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn",
            "HardLight", "SoftLight", "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity",
        ];
        string[] inks = ["0 0 0 1", "1 0.49 0 0", "0 0 0 0.35", "0.46 0 0 0"];
        var text = new StringBuilder("0 0 0 0 k 0 0 612 792 re f\n");
        for (int band = 0; band < 4; band++)
        {
            text.Append(Format("{0} k 0 {1} 612 99 re f\n", inks[band], (band * 198) + 50));
        }

        var states = new StringBuilder("/ExtGState <<");
        var xobjects = new StringBuilder("/XObject <<");
        var objects = new List<string>();
        for (int index = 0; index < modes.Length; index++)
        {
            double x = (index % 4 * 153) + 1;
            double y = index / 4 * 198;
            states.Append(Format(" /B{0} << /BM /{1} >>", index, modes[index]));
            xobjects.Append(Format(" /X{0} {1} 0 R", index, 5 + index));
            text.Append(Format("/B{0} gs /X{0} Do\n", index));

            // A cross: the box's corners cut in, as the GWG patch's 'X'.
            string cross = Format(
                "{0} k {1} {2} m {3} {4} l {5} {2} l {6} {4} l {6} {7} l {5} {8} l {3} {7} l {1} {8} l h f",
                inks[(index + 1) % 4],
                x + 10,
                y + 10,
                x + 75,
                y + 60,
                x + 140,
                x + 140,
                y + 138,
                y + 188);
            objects.Add(ContentSamples.Stream(
                cross,
                Format("/Type /XObject /Subtype /Form /BBox [{0} {1} {2} {3}] /Group << /S /Transparency /CS /DeviceCMYK >>", x, y, x + 150, y + 198)));
        }

        string resources = states + " >> " + xobjects + " >>";
        return ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Group << /S /Transparency /CS /DeviceCMYK >> /Resources << {resources} >> /Contents 4 0 R >>",
            ContentSamples.Stream(text.ToString()),
            .. objects,
        ]);
    }

    private static string Form(string group, string content) =>
        ContentSamples.Stream(content, $"/Type /XObject /Subtype /Form /BBox [0 0 204 198] /Group << /S /Transparency {group} >> /Resources << RESOURCES >>");

    private static string Shading(string coords, string c0, string c1) =>
        $"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords {coords} /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 {c0} /C1 {c1} /N 1 >> >>";

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
