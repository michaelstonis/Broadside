using Broadside.Annotations;
using Broadside.Tests.Graphics;
using Broadside.Tests.Text;
using Broadside.Text;

namespace Broadside.Tests.Annotations;

/// <summary>Marking the changes of a text comparison up as text markup annotations (issue #644).</summary>
public sealed class ComparisonMarkupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void The_old_side_gets_strike_outs_and_the_new_side_highlights_with_one_quad_per_line()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("keep alpha beta", "gamma delta keep", "one two three"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("keep", "keep", "one 2 three inserted", "continues here"));
        TextComparison comparison = old.CompareText(@new, cancellationToken: Token);
        Assert.Equal([TextChangeKind.Deleted, TextChangeKind.Changed, TextChangeKind.Inserted], comparison.Changes.Select(change => change.Kind));

        Assert.Equal(2, old.AddComparisonMarkup(comparison, isNewSide: false));
        Assert.Equal(2, @new.AddComparisonMarkup(comparison, isNewSide: true));

        using PdfDocument oldMarked = PdfDocument.Open(CanvasTests.Saved(old));
        PdfStrikeOutAnnotation[] strikes = [.. oldMarked.Pages[0].Annotations.OfType<PdfStrikeOutAnnotation>()];
        Assert.Equal(2, strikes.Length);
        Assert.Equal(2, strikes[0].QuadPoints.Count); // "alpha beta" and "gamma delta", one per line
        Assert.Equal(("Deleted", null), (strikes[0].Subject, strikes[0].Contents));
        Assert.Single(strikes[1].QuadPoints);
        Assert.Equal(("Changed", "2"), (strikes[1].Subject, strikes[1].Contents)); // what "two" became

        // The quadrilateral of a line covers its words: top-left, top-right, bottom-left, bottom-right.
        PdfQuadrilateral first = strikes[0].QuadPoints[0];
        Assert.True(first.Point1.Y > first.Point3.Y);
        Assert.True(first.Point2.X > first.Point1.X);
        PdfRectangle expected = comparison.Changes[0].OldQuads[0].Bounds;
        Assert.Equal(expected.Left, first.Bounds.Left, 3);
        Assert.Equal(expected.Bottom, first.Bounds.Bottom, 3);
        Assert.Equal(expected.Right, first.Bounds.Right, 3);
        Assert.Equal(expected.Top, first.Bounds.Top, 3);

        using PdfDocument newMarked = PdfDocument.Open(CanvasTests.Saved(@new));
        PdfHighlightAnnotation[] highlights = [.. newMarked.Pages[0].Annotations.OfType<PdfHighlightAnnotation>()];
        Assert.Equal(2, highlights.Length);
        Assert.Equal(("Changed", "two"), (highlights[0].Subject, highlights[0].Contents)); // what "2" replaced
        Assert.Equal(("Inserted", null), (highlights[1].Subject, highlights[1].Contents));
        Assert.Equal(2, highlights[1].QuadPoints.Count);
        Assert.Empty(newMarked.Pages[0].Annotations.OfType<PdfStrikeOutAnnotation>());
    }

    [Fact]
    public void An_identical_comparison_adds_nothing()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("same"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("same"));
        TextComparison comparison = old.CompareText(@new, cancellationToken: Token);

        Assert.Equal(0, old.AddComparisonMarkup(comparison, isNewSide: false));
        Assert.Empty(old.Pages[0].Annotations);
    }

    [Fact]
    public void A_comparison_of_another_document_is_refused_before_anything_is_added()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["one"], ["two"]]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["one"], ["three"]]));
        using PdfDocument other = PdfDocument.Open(ComparisonPdf.Lines("one"));
        TextComparison comparison = old.CompareText(@new, cancellationToken: Token);

        Assert.Throws<ArgumentException>(() => other.AddComparisonMarkup(comparison, isNewSide: false));
        Assert.Empty(other.Pages[0].Annotations);
        Assert.Throws<ArgumentNullException>(() => old.AddComparisonMarkup(null!, isNewSide: false));
    }
}
