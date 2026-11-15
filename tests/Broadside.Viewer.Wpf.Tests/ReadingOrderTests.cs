namespace Broadside.Viewer.Wpf.Tests;

/// <summary>What the automation peers read out: a page's text in reading order, names and states.</summary>
public sealed class ReadingOrderTests
{
    /// <summary>tagged-structure.pdf's page 1 in structure order: heading, paragraph, a two-item list, a two-by-two table.</summary>
    internal const string TaggedStructureText = "Broadside\nA paragraph with a link.\n1.\nFirst item\n2.\nSecond item\nName\nValue\nAlpha\n1";

    [Fact]
    public async Task A_tagged_page_reads_its_leaves_in_structure_order_one_per_line()
    {
        await using DocumentSession session = DocumentSession.Open(Shell.CorpusFile("tagged-structure.pdf"), Shell.Options());
        var bridge = new AccessibilityBridge(session);
        AccessibleNode page = await bridge.LoadPageAsync(0, TestContext.Current.CancellationToken);

        Assert.Equal(TaggedStructureText, ReadingOrder.TextOf(page));
        Assert.Equal("Page 1", ReadingOrder.NameOf(page));
        AccessibleNode heading = page.Children[0].Children[0];
        Assert.Equal(AccessibleRole.Heading, heading.Role);
        Assert.Equal("Broadside", ReadingOrder.NameOf(heading));
    }

    [Fact]
    public void States_read_as_words_in_a_fixed_order()
    {
        Assert.Equal(string.Empty, ReadingOrder.StatusOf(AccessibleStates.Focusable | AccessibleStates.Offscreen));
        Assert.Equal(
            "checked, read only, required, unavailable",
            ReadingOrder.StatusOf(AccessibleStates.Disabled | AccessibleStates.Required | AccessibleStates.ReadOnly | AccessibleStates.Checked));
    }
}
