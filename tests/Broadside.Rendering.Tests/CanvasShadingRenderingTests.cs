using Broadside.Graphics;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Shadings and patterns written through <see cref="PdfCanvas"/>, saved, reopened and observed as pixels of the software
/// rasterizer (ISO 32000-2 §8.7.3, §8.7.4). Pages are 612 x 792 at 72 DPI, so row 641 is user y 150.5.
/// </summary>
public sealed class CanvasShadingRenderingTests
{
    private const int Row = 641;

    [Fact]
    public void A_written_axial_shading_blends_its_stops_along_the_axis()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfAxialShading shading = document.CreateAxialShading(
                new PathPoint(100, 0),
                new PathPoint(500, 0),
                [new PdfGradientStop(0, PdfColor.FromRgb(1, 0, 0)), new PdfGradientStop(1, PdfColor.FromRgb(0, 0, 1))]);
            return canvas =>
            {
                canvas.Rectangle(100, 100, 400, 100);
                canvas.Clip();
                canvas.EndPath();
                canvas.PaintShading(shading);
            };
        });

        AssertNear(new Rgba(255, 0, 0, 255), bitmap.At(100, Row), 4);
        AssertNear(new Rgba(128, 0, 128, 255), bitmap.At(300, Row), 4);
        AssertNear(new Rgba(0, 0, 255, 255), bitmap.At(499, Row), 4);
        AssertNear(new Rgba(255, 255, 255, 255), bitmap.At(50, Row));
    }

    [Fact]
    public void A_written_radial_shading_paints_its_start_colour_at_the_centre_and_extends_its_end_colour()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfRadialShading shading = document.CreateRadialShading(
                new PathPoint(300, 150),
                0,
                new PathPoint(300, 150),
                40,
                [new PdfGradientStop(0, PdfColor.FromRgb(0, 1, 0)), new PdfGradientStop(1, PdfColor.FromRgb(0, 0, 0))],
                new PdfShadingOptions().WithExtend(false, true));
            return canvas =>
            {
                canvas.Rectangle(200, 100, 200, 100);
                canvas.Clip();
                canvas.EndPath();
                canvas.PaintShading(shading);
            };
        });

        AssertNear(new Rgba(0, 255, 0, 255), bitmap.At(300, Row), 8);
        AssertNear(new Rgba(0, 0, 0, 255), bitmap.At(380, Row), 4);
    }

    [Fact]
    public void A_written_shading_pattern_fills_a_path_with_its_shading()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfShadingPattern pattern = document.CreateShadingPattern(document.CreateAxialShading(
                new PathPoint(100, 0),
                new PathPoint(300, 0),
                [new PdfGradientStop(0, PdfColor.FromGray(0)), new PdfGradientStop(1, PdfColor.FromGray(1))]));
            return canvas =>
            {
                canvas.SetFillPattern(pattern);
                canvas.Rectangle(100, 100, 200, 100);
                canvas.Fill();
            };
        });

        AssertNear(new Rgba(0, 0, 0, 255), bitmap.At(100, Row), 4);
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(200, Row), 4);
        AssertNear(new Rgba(255, 255, 255, 255), bitmap.At(350, Row));
    }

    [Fact]
    public void A_written_coloured_tiling_pattern_repeats_its_cell()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfTilingPattern pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20, PdfTilingPaintType.Colored);
            using (PdfCanvas cell = pattern.CreateCanvas())
            {
                cell.SetFillColor(PdfColor.FromRgb(1, 0, 0));
                cell.Rectangle(0, 0, 10, 20);
                cell.Fill();
            }

            return canvas =>
            {
                canvas.SetFillPattern(pattern);
                canvas.Rectangle(100, 100, 200, 100);
                canvas.Fill();
            };
        });

        // Cells start at x = 0, 20, 40, ...: red over the left half of each.
        AssertNear(new Rgba(255, 0, 0, 255), bitmap.At(105, Row));
        AssertNear(new Rgba(255, 255, 255, 255), bitmap.At(115, Row));
        AssertNear(new Rgba(255, 0, 0, 255), bitmap.At(285, Row));
        AssertNear(new Rgba(255, 255, 255, 255), bitmap.At(305, Row));
    }

    [Fact]
    public void A_written_uncoloured_tiling_pattern_paints_its_cell_in_the_colour_given()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfTilingPattern pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20, PdfTilingPaintType.Uncolored);
            using (PdfCanvas cell = pattern.CreateCanvas())
            {
                cell.Rectangle(0, 0, 10, 20);
                cell.Fill();
            }

            return canvas =>
            {
                canvas.SetFillPattern(pattern, PdfColor.FromRgb(0, 0, 1));
                canvas.Rectangle(100, 100, 200, 100);
                canvas.Fill();
            };
        });

        AssertNear(new Rgba(0, 0, 255, 255), bitmap.At(105, Row));
        AssertNear(new Rgba(255, 255, 255, 255), bitmap.At(115, Row));
    }

    [Fact]
    public void Written_mesh_shadings_paint_their_triangles_and_patches()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfColor green = PdfColor.FromRgb(0, 1, 0);
            PdfTriangleMeshShading triangle = document.CreateMeshShading(new PdfFreeFormMeshBuilder(PdfDeviceRgbColorSpace.Instance)
                .AddTriangle(new PdfMeshVertex(100, 100, green), new PdfMeshVertex(200, 100, green), new PdfMeshVertex(100, 200, green)));
            PdfColor blue = PdfColor.FromRgb(0, 0, 1);
            PdfPatchMeshShading patch = document.CreateMeshShading(new PdfPatchMeshBuilder(PdfDeviceRgbColorSpace.Instance, false)
                .AddPatch(
                    [
                        new(300, 100), new(300, 133), new(300, 167), new(300, 200), new(333, 200), new(367, 200),
                        new(400, 200), new(400, 167), new(400, 133), new(400, 100), new(367, 100), new(333, 100),
                    ],
                    [blue, blue, blue, blue]));
            return canvas =>
            {
                canvas.PaintShading(triangle);
                canvas.PaintShading(patch);
            };
        });

        AssertNear(new Rgba(0, 255, 0, 255), bitmap.At(120, Row), 2);
        AssertNear(new Rgba(255, 255, 255, 255), bitmap.At(190, Row - 40));
        AssertNear(new Rgba(0, 0, 255, 255), bitmap.At(350, Row), 2);
    }

    /// <summary>Creates a document, prepares resources, draws on page 1, saves, reopens and renders page 1.</summary>
    private static Bitmap Render(Func<PdfDocument, Action<PdfCanvas>> prepare)
    {
        using var saved = new MemoryStream();
        using (PdfDocument document = PdfDocument.Create())
        {
            Action<PdfCanvas> draw = prepare(document);
            using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
            {
                draw(canvas);
            }

            document.Save(saved);
        }

        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(reopened.Pages[0], new RenderOptions());
    }

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }
}
