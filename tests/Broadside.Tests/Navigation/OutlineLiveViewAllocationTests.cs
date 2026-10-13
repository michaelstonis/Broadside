using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Outline item views are live (issue #286) without costing a walk per read: while nothing the outline's shape was read from has
/// changed, reading the items, children, parents and levels allocates nothing. ISO 32000-2 §12.3.3; ADR 0004.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class OutlineLiveViewAllocationTests
{
    [Fact]
    public void Reading_the_shape_of_an_unchanged_outline_again_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutline outline = document.Outline!;
        PdfOutlineItem section11 = outline.Items[0].Children[0].Children[0];

        long allocated = Allocations.Measure(() =>
        {
            IReadOnlyList<PdfOutlineItem> items = outline.Items;
            for (int index = 0; index < items.Count; index++)
            {
                IReadOnlyList<PdfOutlineItem> children = items[index].Children;
                for (int child = 0; child < children.Count; child++)
                {
                    _ = (children[child].Parent, children[child].Level, children[child].Children.Count);
                }
            }

            _ = (section11.Parent, section11.Level);
        });

        Assert.Equal(0, allocated);
    }
}
