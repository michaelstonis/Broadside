using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>What a shell runs on every pointer move or key press of a selection allocates nothing.</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TextSelectionAllocationTests
{
    [Fact]
    public async Task Dragging_extending_by_keys_and_reading_quads_allocate_nothing()
    {
        await using DocumentSession session = DocumentSession.Open(TextSamples.Page(ContentSamples.TextHeavy(10_000)));
        var selection = new TextSelection(session, ContentReadingOrder.Instance);
        SelectableText text = await selection.LoadPageAsync(0, TestContext.Current.CancellationToken);
        PagePoint start = Centre(text, 10), onText = Centre(text, text.Length / 2), offText = new(0, -50, onText.Y);
        var quads = new Quad[512];
        int found = 0;
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        Action move = () =>
        {
            selection.Begin(start, SelectionGranularity.Word);
            selection.Extend(onText);
            selection.Extend(offText);
            selection.Begin(start);
            selection.Extend(onText);
            selection.Move(SelectionMove.CharacterForward);
            selection.Move(SelectionMove.WordForward);
            selection.Move(SelectionMove.LineDown);
            selection.Move(SelectionMove.LineUp);
            found += selection.GetQuads(0, quads);
            found += text.GetGlyphAt(new PathPoint(onText.X, onText.Y));
            found += selection.FindLink(onText) is null ? 1 : 0;
        };

        Assert.Equal(0, Allocations.Measure(move));
        Assert.True(found > 0);
        Assert.True(changes > 0);
    }

    private static PagePoint Centre(SelectableText text, int position)
    {
        PdfRectangle box = TextSelectionTests.Glyph(text, position).Bounds;
        return new PagePoint(0, (box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2);
    }
}
