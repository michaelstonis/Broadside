using Broadside.Fonts;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Shaping allocates nothing per glyph (issue #174): measuring shaped text runs in a pooled glyph buffer with the shape plan cached
/// per font and shaping, so a long run of ligatures, contextual lookups and Arabic joining allocates nothing once warm.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ShapingAllocationTests
{
    [Fact]
    public void Measuring_shaped_text_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());
        string latin = string.Concat(Enumerable.Repeat("office fifty xy mnp rst uvw zzz kl aeoo d́j q ", 60));
        string arabic = string.Concat(Enumerable.Repeat("باب بتب بلا لا ", 150));
        double width = 0;

        long allocated = Allocations.Measure(() => width = font.MeasureWidth(latin, 10, TextShaping.Default) + font.MeasureWidth(arabic, 10, TextShaping.Default));

        Assert.True(width > 1000, $"{width} points.");
        Assert.Equal(0, allocated);
    }
}
