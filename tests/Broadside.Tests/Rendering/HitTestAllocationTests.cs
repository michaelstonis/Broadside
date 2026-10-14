using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Hit-testing runs on every pointer move of a viewer (viewer capability 4): building a page's index allocates a fixed number of
/// arrays sized to the page, not objects per glyph, and queries allocate nothing. <c>HitTestBenchmarks</c> shows the same.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class HitTestAllocationTests
{
    [Theory]
    [InlineData(1000)]
    [InlineData(10_000)]
    public void Building_the_index_allocates_per_page_not_per_glyph(int glyphs)
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(glyphs)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        Action build = () => list.CreateHitTestIndex();

        long allocated = Allocations.Measure(build);

        // The grid holds an int per cell (about one cell per glyph) and an int per cell a glyph's box touches (a few): no object per
        // glyph (the smallest is 24 bytes) fits in this budget.
        Assert.InRange(allocated, 1, 4096 + (20 * glyphs));
    }

    [Fact]
    public void Queries_allocate_nothing()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(10_000)));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();
        PdfRectangle first = index.GetGlyph(0).Quad.Bounds;
        var point = new PathPoint((first.Left + first.Right) / 2, (first.Bottom + first.Top) / 2);
        long matched = 0;
        Action query = () =>
        {
            foreach (HitTestItem item in index.Find(point, HitTestKinds.All))
            {
                matched++;
            }

            foreach (HitTestItem item in index.Find(new PdfRectangle(point.X, point.Y, point.X + 0.01, point.Y + 0.01), HitTestKinds.Glyphs))
            {
                matched++;
            }

            foreach (HitTestItem item in index.Find(new PdfRectangle(0, 0, 612, 792), HitTestKinds.Glyphs | HitTestKinds.Links))
            {
                matched++;
            }

            matched += index.GetGlyphText(index.GetGlyph(0)).Length > 0 ? 1 : 0;
        };

        long allocated = Allocations.Measure(query);

        Assert.Equal(0, allocated);
        Assert.True(matched > 0);
    }
}
