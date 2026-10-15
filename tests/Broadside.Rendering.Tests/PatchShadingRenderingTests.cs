using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Coons and tensor-product patch meshes (Types 6 and 7) through the software rasterizer (ISO 32000-2 §8.7.4.5.7, §8.7.4.5.8),
/// observed as pixels. Pages are 612 x 792 at 72 DPI, so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y). Expected colours
/// are worked out by hand from the spec's surface and bilinear colour equations and rounded; subdivision may move them by a level
/// or two.
/// </summary>
public sealed class PatchShadingRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    [Fact]
    public void A_coons_patch_mesh_paints_each_patch_with_its_corner_colours_interpolated_bilinearly()
    {
        // shading-type6-coons.pdf, patch 0: corners p00 (100, 100) red, p03 (100, 250) green, p33 (250, 250) blue, p30 (250, 100)
        // black, straight edges, so u = (x − 100) / 150 and v = (y − 100) / 150, and the colour is
        // (1 − u)(1 − v) red + (1 − u) v green + u v blue + u (1 − v) black.
        using Bitmap bitmap = RenderCorpus("shading-type6-coons.pdf");

        // (149.5, 150.5): u = 0.33, v = 0.3367: (0.4444, 0.2256, 0.1111) = (113.3, 57.5, 28.3).
        AssertNear(new Rgba(113, 58, 28, 255), bitmap.At(149, 641));

        // Patch 1 (flag 1) takes p00..p03 from patch 0's top edge: p00 (100, 250) green, p03 (250, 250) blue, p33 (250, 400) red,
        // p30 (100, 400) green, so u runs up y and v along x. At (200.5, 300.5): u = 0.3367, v = 0.67:
        // (1 − u)(1 − v) green + (1 − u) v blue + u v red + u (1 − v) green = (0.2256, 0.3300, 0.4444) = (57.5, 84.2, 113.3).
        AssertNear(new Rgba(58, 84, 113, 255), bitmap.At(200, 491));

        // Outside the L of four patches: unpainted.
        Assert.Equal(White, bitmap.At(300, 691));
        Assert.Equal(White, bitmap.At(99, 641));
        Assert.Equal(White, bitmap.At(300, 141));
    }

    [Theory]
    [InlineData("shading-type6-coons.pdf")]
    [InlineData("shading-type7-tensor.pdf")]
    public async Task Patch_mesh_files_match_their_reviewed_snapshots_at_300_dpi(string fileName)
    {
        // 72 DPI is the golden image (tests/Golden). Reviewed against pdftoppm -r 300: see the 8.7.4.5.7 row of the conformance map.
        using Bitmap bitmap = RenderCorpus(fileName, dpi: 300);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png").UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }

    [Fact]
    public void A_tensor_product_patch_mesh_blends_its_corner_colours_in_its_own_colour_space()
    {
        // shading-type7-tensor.pdf: the same four patches as the Coons file, in DeviceCMYK: patch 0 has p00 cyan, p03 magenta,
        // p33 yellow, p30 black. Through the §10.4.2 formulas (linear until they clip), (149.5, 150.5) at u = 0.33, v = 0.3367
        // is (0.4444, 0.2256, 0.1111, 0.2189) CMYK, so RGB (1 − 0.6633, 1 − 0.4445, 1 − 0.33) = (85.9, 141.7, 170.9).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type7-tensor.pdf"), new PdfOptions().UseColorManagement(ManagedColorManagement.Classic));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(new Rgba(86, 142, 171, 255), bitmap.At(149, 641));
        Assert.Equal(White, bitmap.At(300, 691));
        Assert.NotEqual(White, bitmap.At(300, 341));
    }

    [Fact]
    public void A_patch_folding_over_itself_shows_the_point_with_the_larger_v_then_the_larger_u()
    {
        // Patch A: rows straight in x (100..400, so u = (x − 100) / 300), every column y(v) = 100 (1 − v)³ + 1200 v (1 − v)²
        // + 1200 v² (1 − v) + 200 v³, which rises to 339.4 and falls back to 200: y = 250.5 is reached at v = 0.2105 and again at
        // v = 0.9071. The colour runs red (v = 0) to blue (v = 1), and the larger v must win: (1 − 0.9071, 0, 0.9071) =
        // (23.7, 0, 231.3), not (201.3, 0, 53.7). Below y = 200 there is a single v: at y = 150.5, v = 0.0597, (239.8, 0, 15.2).
        string a = PatchMesh(
            "/ColorSpace /DeviceRGB",
            (0, [(100, 100), (100, 400), (100, 400), (100, 200), (200, 200), (300, 200), (400, 200), (400, 400), (400, 400), (400, 100), (300, 100), (200, 100)], [[255, 0, 0], [0, 0, 255], [0, 0, 255], [255, 0, 0]]));

        // Patch B: the same fold along u, x(u) = 100 (1 − u)³ + 1200 u (1 − u)² + 1200 u² (1 − u) + 200 u³, over y 400..550, red
        // (u = 0) to blue (u = 1): along a line of constant v the larger u must win, so x = 250.5 shows u = 0.9071.
        string b = PatchMesh(
            "/ColorSpace /DeviceRGB",
            (0, [(100, 400), (100, 450), (100, 500), (100, 550), (400, 550), (400, 550), (200, 550), (200, 500), (200, 450), (200, 400), (400, 400), (400, 400)], [[255, 0, 0], [255, 0, 0], [0, 0, 255], [0, 0, 255]]));
        using Bitmap bitmap = RenderContent("/A sh /B sh", "/Shading << /A 5 0 R /B 6 0 R >>", a, b);

        AssertNear(new Rgba(24, 0, 231, 255), bitmap.At(250, 541), tolerance: 2);
        AssertNear(new Rgba(240, 0, 15, 255), bitmap.At(250, 641), tolerance: 2);
        AssertNear(new Rgba(24, 0, 231, 255), bitmap.At(250, 316), tolerance: 2);
    }

    [Fact]
    public void A_patch_mesh_with_a_function_interpolates_t_bilinearly_and_passes_it_through_the_function()
    {
        // A square patch 100..300 with t 0 at p00, 128/255 at p03, 1 at p33, 0 at p30, so t = v (0.5020 (1 − u) + u); the
        // function is red to blue. At (150.5, 250.5), u = 0.2525, v = 0.7525: t = 0.4724, (134.5, 0, 120.5).
        string mesh = PatchMesh(
            "/ColorSpace /DeviceRGB /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>",
            (0, Square(100, 100, 300, 300), [[0], [128], [255], [0]]));
        using Bitmap bitmap = RenderContent("/Sh0 sh", "/Shading << /Sh0 5 0 R >>", mesh);

        AssertNear(new Rgba(134, 0, 120, 255), bitmap.At(150, 541), tolerance: 2);
        Assert.Equal(White, bitmap.At(350, 541));
    }

    [Theory]
    [InlineData(72)]
    [InlineData(97)]
    [InlineData(300)]
    public void Patches_sharing_a_curved_edge_leave_no_crack_between_them(double dpi)
    {
        // Two red patches meet along an S-shaped curve from (300, 100) to (300, 400) through (400, 200) and (200, 300): the left one
        // runs along it downwards as its right edge, the right one upwards as its left edge, each written out in full (flag 0). The
        // right one's outer edge bulges out to x = 1100, so on its own it would want finer cells than the left one. Every pixel
        // between x = 100 (the left one's straight outer edge) and x = 500 is red.
        (int, int)[] curve = [(300, 100), (400, 200), (200, 300), (300, 400)];
        byte[][] red = [[255, 0, 0], [255, 0, 0], [255, 0, 0], [255, 0, 0]];
        string mesh = PatchMesh(
            "/ColorSpace /DeviceRGB",
            (0, [(100, 100), (100, 200), (100, 300), (100, 400), (167, 400), (233, 400), curve[3], curve[2], curve[1], curve[0], (233, 100), (167, 100)], red),
            (0, [curve[0], curve[1], curve[2], curve[3], (367, 400), (433, 400), (500, 400), (1100, 300), (1100, 200), (500, 100), (433, 100), (367, 100)], red));
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes("/Sh0 sh"), "/Shading << /Sh0 5 0 R >>", mesh));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(dpi));

        double scale = dpi / 72;
        int unpainted = 0;
        for (int y = (int)Math.Ceiling((792 - 399) * scale); y < (792 - 101) * scale; y++)
        {
            for (int x = (int)Math.Ceiling(101 * scale); x < 499 * scale; x++)
            {
                unpainted += bitmap.At(x, y) == new Rgba(255, 0, 0, 255) ? 0 : 1;
            }
        }

        Assert.Equal(0, unpainted);
    }

    [Fact]
    public void A_patch_mesh_shading_pattern_paints_its_background_outside_the_patches()
    {
        string mesh = PatchMesh("/ColorSpace /DeviceRGB /Background [0 1 0]", (0, Square(100, 100, 300, 300), [[255, 0, 0], [255, 0, 0], [255, 0, 0], [255, 0, 0]]));
        using Bitmap bitmap = RenderContent("/Pattern cs /P0 scn 50 50 400 400 re f", "/Pattern << /P0 6 0 R >>", mesh, "<< /Type /Pattern /PatternType 2 /Shading 5 0 R >>");

        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(200, 591));
        Assert.Equal(new Rgba(0, 255, 0, 255), bitmap.At(400, 591));
        Assert.Equal(White, bitmap.At(500, 591));
    }

    [Theory]
    [InlineData("shading-type6-coons.pdf")]
    [InlineData("shading-type7-tensor.pdf")]
    [InlineData(null)]
    public void Subdivision_adapts_to_the_scale_and_300_dpi_cuts_at_most_the_stated_factor_of_the_72_dpi_triangles(string? fileName)
    {
        // The stated bound (SoftwareRasterizer.Patches.cs; the 8.7.4.5.7 row of docs/conformance/iso-32000-2.md): a patch is cut
        // into N × N cells, N growing as the square root of the device scale, so with N₇₂ cells per side at 72 DPI the 300 DPI
        // render cuts at most (⌈√(300/72) · N₇₂⌉ / N₇₂)² times as many triangles: 9 when N₇₂ = 1, tending to 300/72 ≈ 4.17. The
        // corpus files have straight edges, so their cells are set by their colours alone and do not change; the curved patch's grow.
        using PdfDocument document = PdfDocument.Open(fileName is null ? CurvedPatchPage() : Corpus.Bytes(fileName));
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        int patches = recorder.Shadings.Select(s => s.Shading).OfType<PdfPatchMeshShading>().Single().PatchCount;
        long at72 = CountTriangles(document, 72);
        long at300 = CountTriangles(document, 300);

        double n72 = Math.Sqrt(at72 / (2.0 * patches));
        Assert.Equal(Math.Round(n72), n72);
        double factor = Math.Pow(Math.Ceiling(Math.Sqrt(300.0 / 72) * n72) / n72, 2);
        Assert.InRange(at300, at72, (long)(factor * at72));
        if (fileName is null)
        {
            Assert.True(at300 > at72, $"The curved patch was cut into {at72} triangles at 72 DPI and {at300} at 300 DPI.");
        }
    }

    private static long CountTriangles(PdfDocument document, double dpi)
    {
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(dpi));
        return rasterizer.PatchTriangleCount;
    }

    /// <summary>One red Coons patch with strongly curved edges over about 0..600 × 0..700.</summary>
    private static byte[] CurvedPatchPage() => ContentSamples.OnePage(
        Encoding.ASCII.GetBytes("/Sh0 sh"),
        "/Shading << /Sh0 5 0 R >>",
        PatchMesh("/ColorSpace /DeviceRGB", (0, [(100, 100), (0, 300), (200, 450), (100, 600), (250, 700), (350, 500), (500, 600), (600, 450), (400, 300), (500, 100), (350, 0), (250, 200)], [[255, 0, 0], [255, 0, 0], [255, 0, 0], [255, 0, 0]])));

    /// <summary>The 12 Coons control points, in stream order, of the straight-edged rectangle (x0, y0)..(x1, y1).</summary>
    private static (int X, int Y)[] Square(int x0, int y0, int x1, int y1)
    {
        int dx = (x1 - x0) / 3;
        int dy = (y1 - y0) / 3;
        return [(x0, y0), (x0, y0 + dy), (x0, y1 - dy), (x0, y1), (x0 + dx, y1), (x1 - dx, y1), (x1, y1), (x1, y1 - dy), (x1, y0 + dy), (x1, y0), (x1 - dx, y0), (x0 + dx, y0)];
    }

    /// <summary>
    /// A Type 6 stream (8-bit flags and components, 16-bit coordinates in user units) of the given patches: each its flag, its 12
    /// control points in stream order and its four corner colours (one component, t, with a Function).
    /// </summary>
    private static string PatchMesh(string entries, params (int Flag, (int X, int Y)[] Points, byte[][] Colors)[] patches)
    {
        var data = new StringBuilder();
        foreach ((int flag, (int X, int Y)[] points, byte[][] colors) in patches)
        {
            data.Append((char)flag);
            foreach ((int x, int y) in points)
            {
                data.Append((char)(x >> 8)).Append((char)(x & 0xFF)).Append((char)(y >> 8)).Append((char)(y & 0xFF));
            }

            foreach (byte[] color in colors)
            {
                foreach (byte c in color)
                {
                    data.Append((char)c);
                }
            }
        }

        string decode = string.Concat(Enumerable.Repeat(" 0 1", patches[0].Colors[0].Length));
        return ContentSamples.Stream(
            data.ToString(),
            $"/ShadingType 6 {entries} /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 65535 0 65535{decode}]");
    }

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && expected.A == actual.A;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }

    private static Bitmap RenderCorpus(string fileName, double dpi = 72)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(dpi));
    }

    [Fact]
    public void Patch_and_free_form_meshes_in_a_cmyk_group_paint_as_they_do_on_the_page()
    {
        // Real-world gate (#138): pdfjs/personwithdog.pdf, pdfjs/issue13520.pdf and the GWG CMYK test pages threw, because the patch
        // grid and the Gouraud subdivision converted colours into three-value buffers inside a DeviceCMYK group (§11.6.6).
        using PdfDocument grouped = PdfDocument.Open(MeshShadingSamples.MeshesInCmykGroup());
        using PdfDocument direct = PdfDocument.Open(MeshShadingSamples.MeshesInCmykGroup(inGroup: false));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap inGroup = rasterizer.Render(grouped.Pages[0]);
        using Bitmap onPage = rasterizer.Render(direct.Pages[0]);

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(30, 760), (100, 692), (170, 622), (60, 560), (140, 470)])
        {
            Rgba a = inGroup.At(x, y);
            Rgba b = onPage.At(x, y);
            Assert.NotEqual(new Rgba(255, 255, 255, 255), b);
            Assert.True(
                Math.Abs(a.R - b.R) <= 24 && Math.Abs(a.G - b.G) <= 24 && Math.Abs(a.B - b.B) <= 24,
                $"({x}, {y}): {a} in the group, {b} on the page");
        }
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
