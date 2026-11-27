namespace Broadside.Rendering.Tests;

/// <summary>The row loop of <see cref="VisualComparison"/> allocates nothing per scanline (issue #644).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class VisualComparisonAllocationTests
{
    [Fact]
    public void Comparing_rows_allocates_nothing()
    {
        const int width = 612, height = 792;
        var old = new byte[width * height * 4];
        var @new = new byte[width * height * 4];
        old.AsSpan().Fill(0xFF);
        @new.AsSpan().Fill(0xFF);
        @new.AsSpan(100 * width * 4, 50 * width * 4).Clear(); // fifty changed rows
        using var overlay = new Bitmap(width, height);
        using var difference = new Bitmap(width, height);
        var cells = new int[((width + 7) / 8) * ((height + 7) / 8)];
        var row = new byte[width];
        long changed = 0;
        Action compare = () => changed = VisualComparison.CompareRows(old, @new, width, height, 16, overlay, difference, cells, row);

        long allocated = Broadside.TestSupport.Allocations.Measure(compare);

        Assert.Equal(0, allocated);
        Assert.Equal(50L * width, changed);
    }
}
