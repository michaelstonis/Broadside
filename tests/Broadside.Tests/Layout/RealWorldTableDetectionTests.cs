using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// Table detection (issue #189) measured cell for cell on real-world untagged tables of the fetched corpora (an unruled table of a
/// paper, a ruled table with a header row, a ruled table with row spans) against the hand labels of <c>LayoutTableLabels.txt</c>.
/// Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldTableDetectionTests
{
    // Measured when detection landed (340 labelled cells in 3 tables of 270, 64 and 6 cells): 339 found at their grid position with
    // their spans, header flag and text (0.997); the miss is one tracemonkey stub whose ligature the text extraction maps to other
    // characters. The floor allows a few more.
    private const double MinimumCellAccuracy = 0.95;

    [Fact]
    public void Detected_tables_match_the_labelled_ones_cell_for_cell()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null || RealWorldCorpus.Directory("pdfium-tests") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int labelled = 0;
        int correct = 0;
        foreach ((string path, int pageNumber, List<Cell> expected) in Sections())
        {
            using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(path));
            LayoutTree tree = document.Pages[pageNumber - 1].ExtractLayout();
            Assert.True(tree.Pages[0].IsDetected, $"{path} page {pageNumber} is detected");

            // The detected table on the page that matches the most labelled cells.
            int best = 0;
            HashSet<Cell> bestFound = [];
            foreach (LayoutTable table in tree.Root.Children.OfType<LayoutTable>())
            {
                HashSet<Cell> found = [.. table.Children.SelectMany(static row => row.Children).Cast<LayoutTableCell>().Select(static cell =>
                    new Cell(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan, cell.IsHeader, Normalize(cell.Text)))];
                int matched = expected.Count(found.Contains);
                if (matched > best)
                {
                    (best, bestFound) = (matched, found);
                }
            }

            labelled += expected.Count;
            correct += best;
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{path} p{pageNumber}: {best}/{expected.Count} cells"));
            foreach (Cell missed in expected.Where(cell => !bestFound.Contains(cell)))
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"  missed {missed}");
            }
        }

        double accuracy = (double)correct / labelled;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{labelled} labelled cells: {correct} correct (accuracy {accuracy:F3})"));
        Assert.True(accuracy >= MinimumCellAccuracy, $"cell accuracy {accuracy:F3}");
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The labelled tables, each cell placed in its grid the way HTML places a table's cells.</summary>
    private static IEnumerable<(string Path, int Page, List<Cell> Cells)> Sections()
    {
        (string Path, int Page, List<Cell> Cells)? current = null;
        var covered = new Dictionary<(int Row, int Column), bool>();
        int row = 0;
        foreach (string line in File.ReadLines(Path.Combine(Path.GetDirectoryName(SourcePath())!, "LayoutTableLabels.txt")))
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                if (current is { } done)
                {
                    yield return done;
                }

                string[] parts = line[3..].Split(' ');
                current = (parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), []);
                covered.Clear();
                row = 0;
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                bool header = line.StartsWith("H ", StringComparison.Ordinal);
                int column = 0;
                foreach (string text in (header ? line[2..] : line).Split(" | "))
                {
                    while (covered.ContainsKey((row, column)))
                    {
                        column++;
                    }

                    (int rowSpan, int columnSpan, string value) = Span(text);
                    for (int r = row; r < row + rowSpan; r++)
                    {
                        for (int c = column; c < column + columnSpan; c++)
                        {
                            covered[(r, c)] = true;
                        }
                    }

                    current!.Value.Cells.Add(new Cell(row, column, rowSpan, columnSpan, header, Normalize(value)));
                    column += columnSpan;
                }

                row++;
            }
        }

        if (current is { } last)
        {
            yield return last;
        }
    }

    private static (int Rows, int Columns, string Text) Span(string cell)
    {
        if (!cell.StartsWith('{'))
        {
            return (1, 1, cell);
        }

        int close = cell.IndexOf('}', StringComparison.Ordinal);
        string[] spans = cell[1..close].Split('x');
        return (int.Parse(spans[0], CultureInfo.InvariantCulture), int.Parse(spans[1], CultureInfo.InvariantCulture), cell[(close + 1)..]);
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    private readonly record struct Cell(int Row, int Column, int RowSpan, int ColumnSpan, bool IsHeader, string Text);
}
