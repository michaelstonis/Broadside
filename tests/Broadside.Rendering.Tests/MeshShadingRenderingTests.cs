using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Function-based (Type 1) and triangle-mesh (Types 4 and 5) shadings through the software rasterizer (ISO 32000-2 §8.7.4.5.2,
/// §8.7.4.5.5, §8.7.4.5.6), observed as pixels. Pages are 612 x 792 at 72 DPI, so pixel (x, y) has its centre at user
/// (x + 0.5, 791.5 − y). Expected colours are worked out by hand from the spec's formulas (bilinear for the corpus's 2 x 2 sampled
/// function, barycentric for Gouraud triangles) and rounded; sampling may move them by a level or two.
/// </summary>
public sealed class MeshShadingRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    [Fact]
    public void A_function_based_shading_maps_its_domain_through_its_matrix_and_paints_the_function_of_each_point()
    {
        // shading-type1-function.pdf: Domain [0 1 0 1], Matrix [200 0 0 200 100 400], a 2 x 2 sampled function: (0,0) red,
        // (1,0) green, (0,1) blue, (1,1) white, so f(u, v) = ((1 − u)(1 − v) + uv, u, v).
        using Bitmap bitmap = RenderCorpus("shading-type1-function.pdf");

        // (149.5, 449.5): u = v = 0.2475, f = (0.6275, 0.2475, 0.2475) = (160, 63, 63).
        AssertNear(new Rgba(160, 63, 63, 255), bitmap.At(149, 342));

        // (280.5, 580.5): u = v = 0.9025, f = (0.8240, 0.9025, 0.9025) = (210, 230, 230).
        AssertNear(new Rgba(210, 230, 230, 255), bitmap.At(280, 211));

        // Near the corners: red at the domain's origin, green at (1, 0), blue at (0, 1). Outside the mapped domain: unpainted.
        AssertNear(new Rgba(255, 0, 0, 255), bitmap.At(100, 391), tolerance: 3);
        AssertNear(new Rgba(0, 255, 0, 255), bitmap.At(299, 391), tolerance: 3);
        AssertNear(new Rgba(0, 0, 255, 255), bitmap.At(100, 192), tolerance: 3);
        Assert.Equal(White, bitmap.At(99, 300));
        Assert.Equal(White, bitmap.At(300, 300));
        Assert.Equal(White, bitmap.At(200, 392));
        Assert.Equal(White, bitmap.At(200, 191));
    }

    [Fact]
    public void A_free_form_mesh_paints_each_triangle_with_its_vertices_colours_interpolated()
    {
        // shading-type4-freeform.pdf: v0 (100, 100) red, v1 (300, 100) green, v2 (200, 300) blue, v3 (400, 300) red,
        // v4 (300, 500) green; flags 0 0 0 1 2 make (v0 v1 v2), (v1 v2 v3), (v1 v3 v4).
        using Bitmap bitmap = RenderCorpus("shading-type4-freeform.pdf");

        // (199.5, 166.5) in (v0 v1 v2): barycentric (0.3362, 0.3312, 0.3325), (85.7, 84.5, 84.8).
        AssertNear(new Rgba(86, 84, 85, 255), bitmap.At(199, 625));

        // (280.5, 260.5) in (v1 v2 v3): (0.1975, 0.4988, 0.3038) of green, blue, red, (77.5, 50.4, 127.2).
        AssertNear(new Rgba(77, 50, 127, 255), bitmap.At(280, 531));

        // (300.5, 233.5) is in (v1 v2 v3) too, but (v1 v3 v4), painted later, covers it: (0.6638, 0.0050, 0.3313) of green,
        // red, green, (1.3, 253.7, 0). And (330.5, 350.5): (0.2213, 0.3050, 0.4738), (77.8, 177.2, 0).
        AssertNear(new Rgba(1, 254, 0, 255), bitmap.At(300, 558));
        AssertNear(new Rgba(78, 177, 0, 255), bitmap.At(330, 441));

        // Outside every triangle: unpainted.
        Assert.Equal(White, bitmap.At(100, 491));
        Assert.Equal(White, bitmap.At(350, 641));
    }

    [Fact]
    public void A_lattice_interpolates_t_across_its_cells_and_passes_it_through_the_function()
    {
        // shading-type5-lattice.pdf: 3 x 3 vertices at x and y 100, 250, 400; t = (column + row) / 4 in 4 bits, so 0, 4/15,
        // 8/15, 11/15, 1; the function is red to blue, (1 − t, 0, t).
        using Bitmap bitmap = RenderCorpus("shading-type5-lattice.pdf");

        // (150.5, 150.5) in (V00 V01 V10): t = 4/15 × (50.5 + 50.5) / 150 = 0.1796, (209.2, 0, 45.8).
        AssertNear(new Rgba(209, 0, 46, 255), bitmap.At(150, 641));

        // (349.5, 349.5) in (V12 V21 V22): weights 0.3367, 0.3367, 0.3267, t = 11/15 × 0.6733 + 0.3267 = 0.8204, (45.8, 0, 209.2).
        AssertNear(new Rgba(46, 0, 209, 255), bitmap.At(349, 442));

        // The lattice covers the square 100..400 only.
        Assert.Equal(White, bitmap.At(99, 500));
        Assert.Equal(White, bitmap.At(250, 391));
    }

    [Fact]
    public void A_truncated_mesh_paints_its_complete_triangles_and_keeps_its_diagnostic()
    {
        // shading-mesh-truncated.pdf: the data of shading-type4-freeform.pdf cut inside v4, so (v1 v3 v4) is lost.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-mesh-truncated.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // Without (v1 v3 v4), (300.5, 233.5) keeps the colour of (v1 v2 v3): (0.3312, 0.3325, 0.3362) of green, blue, red.
        AssertNear(new Rgba(86, 84, 85, 255), bitmap.At(199, 625));
        AssertNear(new Rgba(86, 84, 85, 255), bitmap.At(300, 558));
        Assert.Equal(White, bitmap.At(330, 441));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("MeshDataTruncated", diagnostic.Code);
    }

    [Fact]
    public void The_mesh_limit_of_the_options_cuts_a_larger_mesh_with_the_size_diagnostic()
    {
        // Five vertices with a limit of four: the third triangle needs the fifth, so only (v0 v1 v2) and (v1 v2 v3) paint.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type4-freeform.pdf"), new PdfOptions().WithMaxMeshVertices(4));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(new Rgba(86, 84, 85, 255), bitmap.At(300, 558));
        Assert.Equal(White, bitmap.At(330, 441));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("MeshSizeExceeded", diagnostic.Code);
    }

    [Fact]
    public void A_mesh_without_a_function_interpolates_in_its_own_colour_space_before_converting()
    {
        // A DeviceGray triangle, 0 at (100, 100) and 1 at (500, 100) and (100, 500), so g = (x + y − 200) / 400, through a colour
        // management that converts gray g to RGB (g², g², g²). At (250.5, 150.5) g = 0.5025: interpolating gray and converting
        // gives 0.2525 (64); interpolating the converted vertices would give 0.5025 (128).
        string mesh = FreeFormMesh("/ColorSpace /DeviceGray", (0, 100, 100, [0]), (0, 500, 100, [255]), (0, 100, 500, [255]));
        using PdfDocument document = PdfDocument.Open(
            ContentSamples.OnePage(Encoding.ASCII.GetBytes("/Sh0 sh"), "/Shading << /Sh0 5 0 R >>", mesh),
            new PdfOptions().UseColorManagement(new SquaringColorManagement()));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(new Rgba(64, 64, 64, 255), bitmap.At(250, 641), tolerance: 3);

        // (400.5, 150.5): g = 0.8775, g² = 0.7700 (196).
        AssertNear(new Rgba(196, 196, 196, 255), bitmap.At(400, 641), tolerance: 3);
    }

    [Fact]
    public void A_mesh_shading_pattern_paints_its_background_outside_the_triangles_and_sh_does_not()
    {
        // One red triangle (100, 100), (300, 100), (100, 300), Background green, filling the square 50..350 as a pattern.
        string mesh = FreeFormMesh(
            "/ColorSpace /DeviceRGB /Background [0 1 0]",
            (0, 100, 100, [255, 0, 0]),
            (0, 300, 100, [255, 0, 0]),
            (0, 100, 300, [255, 0, 0]));
        using Bitmap sh = RenderContent("/Sh0 sh", "/Shading << /Sh0 5 0 R >>", mesh);
        using Bitmap pattern = RenderContent(
            "/Pattern cs /P0 scn 50 50 300 300 re f",
            "/Pattern << /P0 6 0 R >>",
            mesh,
            "<< /Type /Pattern /PatternType 2 /Shading 5 0 R >>");

        Rgba red = new(255, 0, 0, 255);
        Rgba green = new(0, 255, 0, 255);
        Assert.Equal(red, sh.At(150, 641));
        Assert.Equal(White, sh.At(250, 541));
        Assert.Equal(red, pattern.At(150, 641));
        Assert.Equal(green, pattern.At(250, 541));
        Assert.Equal(green, pattern.At(60, 731));
        Assert.Equal(White, pattern.At(400, 641));
    }

    [Fact]
    public void A_function_based_shading_pattern_paints_its_background_outside_the_domain_and_the_bounding_box()
    {
        // Domain [0 1 0 1] mapped onto 100..300, f = (x, 0, 1 − x); BBox 0..200 × 0..800; the pattern fills 50..350 × 150..350.
        using Bitmap bitmap = RenderContent(
            "/Pattern cs /P0 scn 50 150 300 200 re f",
            "/Pattern << /P0 6 0 R >>",
            "<< /ShadingType 1 /ColorSpace /DeviceRGB /Matrix [200 0 0 200 100 100] /BBox [0 0 200 800] /Background [0 1 0] /Function 7 0 R >>",
            "<< /Type /Pattern /PatternType 2 /Shading 5 0 R >>",
            ContentSamples.Stream("\x00\x00\xFF\xFF\x00\x00\x00\x00\xFF\xFF\x00\x00", "/FunctionType 0 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] /Size [2 2] /BitsPerSample 8"));

        // (150.5, 250.5): x = 0.2525 of the domain, (64, 0, 191).
        AssertNear(new Rgba(64, 0, 191, 255), bitmap.At(150, 541), tolerance: 2);

        Rgba green = new(0, 255, 0, 255);
        Assert.Equal(green, bitmap.At(60, 541));
        Assert.Equal(green, bitmap.At(250, 541));
        Assert.Equal(White, bitmap.At(400, 541));
    }

    [Fact]
    public void A_mesh_composites_through_the_constant_alpha()
    {
        // ca 0.5 over white: a red triangle paints (255, 128, 128).
        string mesh = FreeFormMesh("/ColorSpace /DeviceRGB", (0, 100, 100, [255, 0, 0]), (0, 300, 100, [255, 0, 0]), (0, 100, 300, [255, 0, 0]));
        using Bitmap bitmap = RenderContent("/GS0 gs /Sh0 sh", "/Shading << /Sh0 5 0 R >> /ExtGState << /GS0 6 0 R >>", mesh, "<< /ca 0.5 >>");

        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(150, 641));
        Assert.Equal(White, bitmap.At(250, 541));
    }

    /// <summary>Converts gray g to RGB (g², g², g²), a conversion that is not linear.</summary>
    private sealed class SquaringColorManagement : IColorManagement
    {
        public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion) => new SquaringConverter();

        private sealed class SquaringConverter : IColorConverter
        {
            public int InputCount => 1;

            public int OutputCount => 3;

            public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float g = source[i] * source[i];
                    destination[3 * i] = g;
                    destination[(3 * i) + 1] = g;
                    destination[(3 * i) + 2] = g;
                }
            }
        }
    }

    /// <summary>A Type 4 stream (8-bit flags and components, 16-bit coordinates in user units) of the given vertices.</summary>
    private static string FreeFormMesh(string entries, params (int Flag, int X, int Y, byte[] Color)[] vertices)
    {
        var data = new StringBuilder();
        int components = vertices[0].Color.Length;
        foreach ((int flag, int x, int y, byte[] color) in vertices)
        {
            data.Append((char)flag).Append((char)(x >> 8)).Append((char)(x & 0xFF)).Append((char)(y >> 8)).Append((char)(y & 0xFF));
            foreach (byte c in color)
            {
                data.Append((char)c);
            }
        }

        string decode = string.Concat(Enumerable.Repeat(" 0 1", components));
        return ContentSamples.Stream(
            data.ToString(),
            $"/ShadingType 4 {entries} /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 65535 0 65535{decode}]");
    }

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && expected.A == actual.A;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
