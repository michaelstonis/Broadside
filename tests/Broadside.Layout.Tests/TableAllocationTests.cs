using Broadside.Fonts;
using Broadside.Layout.Flow;
using Broadside.TestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// A tagged table over many pages costs memory in proportion to its rows: a step that measured, placed or tagged the whole table
/// again per row or per page would grow with the square of the rows (issue #541, the <c>Table10k</c> benchmarks of issue #397).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TableAllocationTests
{
    private const int Rows = 500;
    private const int Scale = 4;

    [Fact]
    public void Four_times_the_rows_allocate_about_four_times_the_bytes()
    {
        Action small = () => Render(Rows);
        Action large = () => Render(Rows * Scale);

        long smallBytes = Allocations.Measure(small, warmUpCalls: 3);
        long largeBytes = Allocations.Measure(large, warmUpCalls: 3);

        // Linear is 4x (a little less: the document and the fonts are shared); quadratic would be 16x.
        double ratio = (double)largeBytes / smallBytes;
        Assert.True(ratio < Scale * 1.15, $"{Rows} rows allocated {smallBytes:N0} B, {Rows * Scale} rows {largeBytes:N0} B: {ratio:F2}x.");
    }

    /// <summary>The <c>Table10kFlow</c> benchmark's table with <paramref name="rows"/> body rows: three columns, a repeated header, tagged.</summary>
    private static void Render(int rows)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        var cell = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 11);
        var header = new ParagraphStyle(document.GetStandard14Font(Standard14Font.HelveticaBold), 11);
        var table = new Table(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1)) { HeaderRows = 1, Style = Theme.Default.Table };
        table.AddRow(new TableCell("Item", header), new TableCell("Qty", header), new TableCell("Amount", header));
        for (int row = 0; row < rows; row++)
        {
            table.AddRow(new TableCell($"Item {row}", cell), new TableCell($"{row % 97}", cell), new TableCell($"{row * 3 % 1000}.00", cell));
        }

        var flow = new FlowDocument(PageSetup.A4) { Tagging = FlowTagging.Default.WithLanguage("en") };
        flow.Add(table);
        flow.Render(document);
    }
}
