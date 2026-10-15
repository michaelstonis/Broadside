using System.Globalization;
using System.Runtime.CompilerServices;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// Layout detection (issue #187) measured on real-world untagged pages of the fetched corpora (a two-column paper, a book with
/// running headers, two forms) against the hand-labelled paragraphs of <c>LayoutDetectionLabels.txt</c>. Skips when the corpora
/// are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldLayoutDetectionTests
{
    // Measured when the detector landed (76 labels on 5 pages): 67 found (0.88), 63 in order (recall 0.83), 77 detected (precision
    // 0.82); the paper and the book pages are exact, the forms lose checkbox options and row-wise order. The floors leave a little room.
    private const double MinimumRecall = 0.80;
    private const double MinimumPrecision = 0.78;

    [Fact]
    public void Detected_paragraphs_match_the_labelled_reading_order()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var trees = new Dictionary<string, (PdfDocument Document, LayoutTree Tree)>(StringComparer.Ordinal);
        int labels = 0;
        int found = 0;
        int ordered = 0;
        int detected = 0;
        try
        {
            foreach ((string path, int pageNumber, List<string> expected) in Sections())
            {
                if (!trees.TryGetValue(path, out (PdfDocument Document, LayoutTree Tree) entry))
                {
                    PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(path));
                    trees.Add(path, entry = (document, document.ExtractLayout()));
                }

                string[] paragraphs = [.. entry.Tree.Root.Children
                    .Where(node => node.Extents[0].Page.PageIndex == pageNumber - 1)
                    .Select(static node => Normalize(node.Text))];
                Assert.True(entry.Tree.Pages[pageNumber - 1].IsDetected, $"{path} page {pageNumber} is detected");
                int position = 0;
                int pageOrdered = 0;
                foreach (string label in expected.Select(Normalize))
                {
                    found += paragraphs.Any(paragraph => paragraph.StartsWith(label, StringComparison.Ordinal)) ? 1 : 0;
                    int at = Array.FindIndex(paragraphs, position, paragraph => paragraph.StartsWith(label, StringComparison.Ordinal));
                    if (at >= 0)
                    {
                        pageOrdered++;
                        position = at + 1;
                    }
                }

                labels += expected.Count;
                ordered += pageOrdered;
                detected += paragraphs.Length;
                TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"{path} p{pageNumber}: {pageOrdered}/{expected.Count} in order, {paragraphs.Length} detected"));
            }
        }
        finally
        {
            foreach ((PdfDocument document, _) in trees.Values)
            {
                document.Dispose();
            }
        }

        double recall = (double)ordered / labels;
        double precision = (double)ordered / detected;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{labels} labels: found {found} ({(double)found / labels:F2}), in order {ordered} (recall {recall:F2}), {detected} detected (precision {precision:F2})"));
        Assert.True(recall >= MinimumRecall, $"recall {recall:F2}");
        Assert.True(precision >= MinimumPrecision, $"precision {precision:F2}");
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<(string Path, int Page, List<string> Labels)> Sections()
    {
        (string Path, int Page, List<string> Labels)? current = null;
        foreach (string line in File.ReadLines(Path.Combine(Path.GetDirectoryName(SourcePath())!, "LayoutDetectionLabels.txt")))
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                if (current is { } done)
                {
                    yield return done;
                }

                string[] parts = line[3..].Split(' ');
                current = (parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), []);
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                current!.Value.Labels.Add(line);
            }
        }

        if (current is { } last)
        {
            yield return last;
        }
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
}
