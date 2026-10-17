using Broadside.Graphics;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Transparency groups and soft masks written through <see cref="PdfCanvas"/>, saved, reopened and observed as pixels of the
/// software rasterizer (ISO 32000-2 §11.6.5.1, §11.6.6). Pages are 612 x 792 at 72 DPI, so row 641 is user y 150.5.
/// </summary>
public sealed class CanvasGroupRenderingTests
{
    private const int Row = 641;

    [Fact]
    public void A_written_knockout_group_paints_its_later_object_over_the_backdrop_only()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfFormXObject group = document.CreateFormXObject(new PdfRectangle(0, 0, 612, 792));
            group.CreateTransparencyGroup().IsKnockout = true;
            using (PdfCanvas canvas = group.CreateCanvas())
            {
                canvas.SetFillAlpha(0.5);
                canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
                canvas.Rectangle(100, 100, 100, 100);
                canvas.Fill();
                canvas.SetFillColor(PdfColor.FromRgb(0, 0, 1));
                canvas.Rectangle(150, 100, 100, 100);
                canvas.Fill();
            }

            return canvas => canvas.DrawForm(group);
        });

        // Red at 0.5 alone; in the overlap the blue knocks the red out and composites with the white backdrop only.
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(125, Row));
        AssertNear(new Rgba(128, 128, 255, 255), bitmap.At(175, Row));
    }

    [Fact]
    public void A_written_isolated_group_drawn_at_half_opacity_composites_once_as_a_whole()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfFormXObject group = document.CreateFormXObject(new PdfRectangle(0, 0, 612, 792));
            group.CreateTransparencyGroup().IsIsolated = true;
            using (PdfCanvas canvas = group.CreateCanvas())
            {
                canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
                canvas.Rectangle(300, 100, 100, 100);
                canvas.Fill();
                canvas.Rectangle(350, 100, 100, 100);
                canvas.Fill();
            }

            return canvas =>
            {
                canvas.SetFillAlpha(0.5);
                canvas.DrawForm(group);
            };
        });

        // The overlap is no darker than either square alone: the group's opacity applies to its result.
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(325, Row));
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(375, Row));
    }

    [Fact]
    public void A_written_luminosity_mask_scales_the_paint_inside_its_group_and_hides_it_outside()
    {
        using Bitmap bitmap = Render(document =>
        {
            PdfFormXObject group = document.CreateFormXObject(new PdfRectangle(500, 100, 550, 200));
            group.CreateTransparencyGroup().ColorSpace = PdfColor.FromGray(0).ColorSpace;
            using (PdfCanvas canvas = group.CreateCanvas())
            {
                canvas.SetFillColor(PdfColor.FromGray(0.5f));
                canvas.Rectangle(500, 100, 50, 100);
                canvas.Fill();
            }

            PdfSoftMask mask = document.CreateSoftMask(PdfSoftMaskType.Luminosity, group);
            return canvas =>
            {
                canvas.SetSoftMask(mask);
                canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
                canvas.Rectangle(500, 100, 100, 100);
                canvas.Fill();
            };
        });

        // Luminosity 0.5 inside the group's bounding box; the default black backdrop masks everything outside it (§11.5.3).
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(525, Row));
        Assert.Equal(new Rgba(255, 255, 255, 255), bitmap.At(575, Row));
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
