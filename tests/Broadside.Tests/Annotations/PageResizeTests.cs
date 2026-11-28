using System.Text;
using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Tests.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>
/// Resizing a page, scaling its content and adding margins in place (issue #647): one matrix applied to the content (ISO 32000-2
/// §8.3.4, §7.8.2), the boxes (§14.11.2) and the geometry of every annotation (§12.5.2), <c>NoZoom</c> kept (§12.5.3).
/// </summary>
public sealed class PageResizeTests
{
    private static readonly CosName[] FlatKeys = [new("L"), new("Vertices"), new("QuadPoints"), new("CL")];
    private static readonly CosName[] NestedKeys = [new("InkList"), new("Path")];

    [Theory]
    [InlineData(PdfContentFit.Fit, 306, 396, 0.5, 0.5, 0, 0)]
    [InlineData(PdfContentFit.Fill, 612, 396, 1, 1, 0, -198)]
    [InlineData(PdfContentFit.Stretch, 306, 792, 0.5, 1, 0, 0)]
    [InlineData(PdfContentFit.None, 306, 396, 1, 1, -153, -198)]
    [InlineData(PdfContentFit.Fit, 595, 842, 0.9722, 0.9722, 0, 36.0)]
    public void Resize_sets_the_boxes_and_wraps_the_content_in_the_fitted_matrix(PdfContentFit fit, double width, double height, double a, double d, double e, double f)
    {
        using PdfDocument document = ImpositionTests.Source(1);
        PdfPage page = document.Pages[0];

        page.Resize(width, height, fit);

        Assert.Equal(new PdfRectangle(0, 0, width, height), page.MediaBox);
        Assert.Equal(new PdfRectangle(0, 0, width, height), page.CropBox);
        CosArray contents = Assert.IsType<CosArray>(document.Resolve(page.Dictionary[KnownNames.Contents]));
        double[] matrix = Numbers(Text(document, contents[0]).Replace("q", "", StringComparison.Ordinal).Replace("cm", "", StringComparison.Ordinal));
        double[] expected = [a, 0, 0, d, e, f];
        Assert.Equal(6, matrix.Length);
        for (int index = 0; index < 6; index++)
        {
            Assert.Equal(expected[index], matrix[index], 0.01);
        }

        Assert.Equal("Q", Text(document, contents[^1]).Trim());

        // The label at the old page's center lands where the matrix puts that center.
        PathPoint center = new Matrix(a, 0, 0, d, e, f).Transform(306, 396);
        PdfRectangle label = page.ExtractText().Words[0].Quad.Bounds;
        Assert.InRange((label.Left + label.Right) / 2, center.X - 12, center.X + 12);
    }

    [Fact]
    public void Resize_takes_the_size_as_displayed_for_a_rotated_page_and_maps_the_other_boxes()
    {
        using PdfDocument document = ImpositionTests.Source(1);
        PdfPage page = document.Pages[0];
        page.Rotation = 90;
        page.TrimBox = new PdfRectangle(10, 10, 602, 782);

        page.Resize(396, 306);

        Assert.Equal(new PdfRectangle(0, 0, 306, 396), page.MediaBox);
        Assert.Equal(new PdfRectangle(5, 5, 301, 391), page.TrimBox);
    }

    [Fact]
    public void ScaleContent_scales_about_the_crop_box_center_and_keeps_the_boxes()
    {
        using PdfDocument document = ImpositionTests.Source(1);
        PdfPage page = document.Pages[0];
        PdfRectangle media = page.MediaBox;

        page.ScaleContent(0.5);

        Assert.Equal(media, page.MediaBox);
        CosArray contents = Assert.IsType<CosArray>(document.Resolve(page.Dictionary[KnownNames.Contents]));
        Assert.Equal("q 0.5 0 0 0.5 153 198 cm", Text(document, contents[0]).Trim());
        Assert.Throws<ArgumentOutOfRangeException>(() => page.ScaleContent(0));
    }

    [Fact]
    public void AddMargins_grows_the_boxes_and_translates_the_content_without_scaling()
    {
        using PdfDocument document = ImpositionTests.Source(1);
        PdfPage page = document.Pages[0];
        PdfRectangle before = page.ExtractText().Words[0].Quad.Bounds;

        page.AddMargins(10, 20, 30, 40);

        Assert.Equal(new PdfRectangle(0, 0, 652, 852), page.MediaBox);
        Assert.Equal(new PdfRectangle(0, 0, 652, 852), page.CropBox);
        PdfRectangle after = page.ExtractText().Words[0].Quad.Bounds;
        Assert.Equal(before.Left + 10, after.Left, 2);
        Assert.Equal(before.Bottom + 20, after.Bottom, 2);
        Assert.Equal(before.Width, after.Width, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => page.AddMargins(-1, 0, 0, 0));
    }

    [Theory]
    [InlineData(90, 672, 832, 40, 10)]
    [InlineData(180, 652, 852, 30, 40)]
    [InlineData(270, 672, 832, 20, 30)]
    public void AddMargins_are_given_as_the_page_is_displayed(int rotation, double width, double height, double dx, double dy)
    {
        using PdfDocument document = ImpositionTests.Source(1);
        PdfPage page = document.Pages[0];
        page.Rotation = rotation;
        PdfRectangle before = page.ExtractText().Words[0].Quad.Bounds;

        page.AddMargins(10, 20, 30, 40);

        Assert.Equal(new PdfRectangle(0, 0, width, height), page.MediaBox);
        PdfRectangle after = page.ExtractText().Words[0].Quad.Bounds;
        Assert.Equal(before.Left + dx, after.Left, 2);
        Assert.Equal(before.Bottom + dy, after.Bottom, 2);
    }

    public static TheoryData<string> Operations => ["Resize", "ScaleContent", "AddMargins"];

    [Theory]
    [MemberData(nameof(Operations))]
    public void Every_geometric_annotation_follows_the_content_and_NoZoom_keeps_its_size(string operation)
    {
        using PdfDocument original = PdfDocument.Open(Corpus.Bytes("annotations-subtypes.pdf"));
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-subtypes.pdf"));
        PdfPage page = document.Pages[0];
        Matrix matrix;
        switch (operation)
        {
            case "Resize":
                page.Resize(306, 396);
                matrix = Matrix.CreateScale(0.5, 0.5);
                break;
            case "ScaleContent":
                page.ScaleContent(2);
                matrix = new Matrix(2, 0, 0, 2, -306, -396);
                break;
            default:
                page.AddMargins(10, 20, 30, 40);
                matrix = Matrix.CreateTranslation(10, 20);
                break;
        }

        int checkedPoints = 0;
        var before = original.Pages[0].Annotations;
        var after = page.Annotations;
        Assert.Equal(before.Count, after.Count);
        for (int index = 0; index < before.Count; index++)
        {
            CosDictionary old = before[index].Dictionary;
            CosDictionary mapped = after[index].Dictionary;
            PdfRectangle oldRect = before[index].Rect;
            bool keepsSize = before[index].Kind == PdfAnnotationKind.Text || (before[index].Flags & PdfAnnotationFlags.NoZoom) != 0;
            Matrix used = matrix;
            if (keepsSize)
            {
                PathPoint corner = matrix.Transform(oldRect.Left, oldRect.Top);
                used = Matrix.CreateTranslation(corner.X - oldRect.Left, corner.Y - oldRect.Top);
                Assert.Equal(oldRect.Width, after[index].Rect.Width, 3);
                Assert.Equal(oldRect.Height, after[index].Rect.Height, 3);
            }

            AssertRect(AnnotationGeometryBounds(used, oldRect), after[index].Rect);
            foreach (CosName key in FlatKeys)
            {
                if (old.ContainsKey(key))
                {
                    checkedPoints += AssertMapped(original, document, old[key], mapped[key], used);
                }
            }

            foreach (CosName key in NestedKeys)
            {
                if (old.ContainsKey(key))
                {
                    var oldLists = (CosArray)original.Resolve(old[key]);
                    var newLists = (CosArray)document.Resolve(mapped[key]);
                    for (int list = 0; list < oldLists.Count; list++)
                    {
                        checkedPoints += AssertMapped(original, document, oldLists[list], newLists[list], used);
                    }
                }
            }
        }

        // L, Vertices, Path, QuadPoints (link, four markups, redact), InkList and CL were all seen.
        Assert.True(checkedPoints >= 30, $"{checkedPoints} points checked.");
        Assert.Contains(before, annotation => annotation.Kind == PdfAnnotationKind.Text && (annotation.Flags & PdfAnnotationFlags.NoZoom) != 0);
    }

    [Fact]
    public void Rectangle_differences_scale_with_the_content()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-subtypes.pdf"));
        PdfPage page = document.Pages[0];

        page.ScaleContent(0.5);

        PdfAnnotation square = page.Annotations.First(annotation => annotation.Kind == PdfAnnotationKind.Square);
        var rd = (CosArray)document.Resolve(square.Dictionary[new CosName("RD")]);
        Assert.Equal([0.5, 1, 1.5, 2], rd.Select(value => ((CosNumber)document.Resolve(value)).ToDouble()));
    }

    [Fact]
    public void The_reshaped_page_saves_and_reopens_without_diagnostics()
    {
        using PdfDocument document = ImpositionTests.Source(1);
        document.Pages[0].AddSquareAnnotation(new PdfRectangle(100, 100, 200, 200));
        document.Pages[0].Resize(595, 842);

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), reopened.Pages[0].MediaBox);
        Assert.Contains("P1", reopened.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(reopened.Diagnostics);
    }

    private static PdfRectangle AnnotationGeometryBounds(Matrix matrix, PdfRectangle rect)
    {
        PathPoint a = matrix.Transform(rect.Left, rect.Bottom);
        PathPoint b = matrix.Transform(rect.Right, rect.Top);
        return new PdfRectangle(a.X, a.Y, b.X, b.Y);
    }

    private static void AssertRect(PdfRectangle expected, PdfRectangle actual)
    {
        Assert.Equal(expected.Left, actual.Left, 3);
        Assert.Equal(expected.Bottom, actual.Bottom, 3);
        Assert.Equal(expected.Right, actual.Right, 3);
        Assert.Equal(expected.Top, actual.Top, 3);
    }

    private static int AssertMapped(PdfDocument original, PdfDocument document, CosObject before, CosObject after, Matrix matrix)
    {
        var oldPoints = (CosArray)original.Resolve(before);
        var newPoints = (CosArray)document.Resolve(after);
        Assert.Equal(oldPoints.Count, newPoints.Count);
        for (int index = 0; index + 1 < oldPoints.Count; index += 2)
        {
            PathPoint expected = matrix.Transform(((CosNumber)oldPoints[index]).ToDouble(), ((CosNumber)oldPoints[index + 1]).ToDouble());
            Assert.Equal(expected.X, ((CosNumber)newPoints[index]).ToDouble(), 3);
            Assert.Equal(expected.Y, ((CosNumber)newPoints[index + 1]).ToDouble(), 3);
        }

        return oldPoints.Count / 2;
    }

    private static string Text(PdfDocument document, CosObject stream) =>
        Encoding.ASCII.GetString(document.DecodeStream((CosStream)document.Resolve(stream)!).Span);

    private static double[] Numbers(string text) =>
        [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture))];
}
