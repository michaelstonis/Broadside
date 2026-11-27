using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// A page drawn through the form XObject made from it (issue #628) renders as the page itself does, pixel for pixel, with the
/// software rasterizer: the form's bounding box, matrix (rotation, user unit, origin), resources, group and optional content keep
/// what the page shows. ISO 32000-2 §8.10.1, §8.10.3, §14.11.2.
/// </summary>
public sealed class PageFormRenderingTests
{
    [Theory]
    [InlineData("page-rotation.pdf", 0)]
    [InlineData("page-rotation.pdf", 1)]
    [InlineData("page-rotation.pdf", 2)]
    [InlineData("page-rotation.pdf", 3)]
    [InlineData("transparency-groups.pdf", 0)]
    [InlineData("pattern-in-form.pdf", 0)]
    [InlineData("optional-content.pdf", 0)]
    public async Task A_page_drawn_through_its_imported_form_matches_the_page(string fileName, int page)
    {
        using PdfDocument source = await PdfDocument.OpenAsync(Corpus.Bytes(fileName), TestContext.Current.CancellationToken);
        using PdfDocument target = PdfDocument.Create();
        PdfFormXObject form = target.CreateFormXObject(source.Pages[page]);
        PdfRectangle box = form.BoundingBox!.Value;
        PathPoint a = form.Matrix.Transform(box.Left, box.Bottom);
        PathPoint b = form.Matrix.Transform(box.Right, box.Top);
        PdfPage drawn = target.Pages.Add(new PdfRectangle(0, 0, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)));
        using (PdfCanvas canvas = drawn.CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        using var output = new MemoryStream();
        await target.SaveAsync(output, TestContext.Current.CancellationToken);
        using PdfDocument reopened = await PdfDocument.OpenAsync(output.ToArray(), TestContext.Current.CancellationToken);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(source.Pages[page]);
        using Bitmap actual = rasterizer.Render(reopened.Pages[reopened.Pages.Count - 1]);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++)
        {
            Assert.True(expected.GetRow(y).SequenceEqual(actual.GetRow(y)), $"Row {y} differs.");
        }

        Assert.Empty(reopened.Diagnostics);
        using MemoryStream png = PngSnapshots.Encode(actual);
        await Verify(png, "png").UseParameters(Path.GetFileNameWithoutExtension(fileName), page);
    }
}
