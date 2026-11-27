using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Tests.Content;
using Broadside.Text;

namespace Broadside.Tests.Annotations;

/// <summary>
/// Reviewing search matches as Redact annotations (issue #633, ISO 32000-2 §12.5.6.23, Table 195): one annotation per match with a
/// quadrilateral per line, the overlay entries from the options, and the matched value kept out of <c>Contents</c> by default.
/// </summary>
public sealed class RedactAnnotationsFromSearchTests
{
    private const string TwoLines = "BT /F1 10 Tf 72 700 Td (Card 4111 1111) Tj 0 -12 Td (1111 1111 ends) Tj ET BT /F1 10 Tf 72 600 Td (Mail jane@example.com) Tj ET";

    [Fact]
    public void Each_match_gets_one_annotation_with_a_quadrilateral_per_line()
    {
        using PdfDocument document = RedactionSamples.Open(TwoLines);
        PdfPage page = document.Pages[0];
        TextMatch match = Assert.Single(page.FindText(TextPatterns.CreditCardNumber));

        IReadOnlyList<PdfRedactAnnotation> annotations = document.AddRedactAnnotations(TextPatterns.CreditCardNumber);

        PdfRedactAnnotation annotation = Assert.Single(annotations);
        Assert.Equal(2, annotation.QuadPoints.Count);
        for (int line = 0; line < 2; line++)
        {
            Quad expected = match.Quads[line];
            PdfQuadrilateral actual = annotation.QuadPoints[line];
            AssertNear(expected.BottomLeft, actual.Point1);
            AssertNear(expected.BottomRight, actual.Point2);
            AssertNear(expected.TopRight, actual.Point3);
            AssertNear(expected.TopLeft, actual.Point4);
        }

        Assert.Same(page, annotation.Page);
        Assert.Contains(annotation, page.Annotations);
    }

    [Fact]
    public void Contents_is_not_written_by_default()
    {
        using PdfDocument document = RedactionSamples.Open(TwoLines);

        PdfRedactAnnotation annotation = Assert.Single(document.AddRedactAnnotations(TextPatterns.EmailAddress));

        Assert.Null(annotation.Contents);
        Assert.Null(annotation.OverlayText);
        Assert.Equal(new PdfDeviceColor(0), annotation.InteriorColor);
    }

    [Fact]
    public void Overlay_text_and_fill_come_from_the_options_and_the_match_goes_into_Contents()
    {
        using PdfDocument document = RedactionSamples.Open(TwoLines);
        PdfRedactionOptions options = new PdfRedactionOptions().WithOverlayText("REDACTED").WithFillColor(new PdfDeviceColor(1, 0, 0));

        PdfRedactAnnotation annotation = Assert.Single(document.AddRedactAnnotations(TextPatterns.EmailAddress, options));

        Assert.Equal("REDACTED", annotation.OverlayText);
        Assert.Equal(new PdfDeviceColor(1, 0, 0), annotation.InteriorColor);
        Assert.Equal("jane@example.com", annotation.Contents);
    }

    [Fact]
    public void No_match_adds_nothing()
    {
        using PdfDocument document = RedactionSamples.Open(TwoLines);

        Assert.Empty(document.AddRedactAnnotations(TextPatterns.Iban));
        Assert.Empty(document.Pages[0].Annotations);
    }

    [Fact]
    public void Applying_the_annotations_later_removes_the_marked_text()
    {
        using PdfDocument document = RedactionSamples.Open(TwoLines);
        document.AddRedactAnnotations(TextPatterns.EmailAddress);

        PdfRedactionResult result = document.Redact();

        Assert.Equal(1, result.AnnotationsRemoved);
        Assert.DoesNotContain("jane", document.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Pages[0].Annotations);
    }

    private static void AssertNear(PathPoint expected, PathPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
    }
}
