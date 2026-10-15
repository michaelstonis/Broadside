using System.Globalization;
using System.Runtime.CompilerServices;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// Layout detection (issues #187 and #188) measured on real-world untagged pages of the fetched corpora (a two-column paper, a book
/// with running headers, two forms) against the hand labels of <c>LayoutDetectionLabels.txt</c>: paragraphs in reading order, and
/// which of them are headings and list items. Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldLayoutDetectionTests : IDisposable
{
    // Measured when the detector landed (76 labels on 5 pages): 67 found (0.88), 63 in order (recall 0.83), 77 detected (precision
    // 0.82); the paper and the book pages are exact, the forms lose checkbox options and row-wise order. The floors leave a little room.
    private const double MinimumRecall = 0.80;
    private const double MinimumPrecision = 0.78;

    // Headings and list items (#188), measured when they landed (12 heading and 18 list item labels on 8 pages: every heading and
    // bullet of tracemonkey pages 1, 2, 6, 8 and 11, and the forms' titles): all found, none extra (recall and precision 1.00 for
    // each kind), every item at its labelled depth; the paragraph measure above did not change. The floors allow a miss of each kind.
    private const double MinimumHeadingRecall = 0.90;
    private const double MinimumHeadingPrecision = 0.90;
    private const double MinimumListRecall = 0.90;
    private const double MinimumListPrecision = 0.90;

    private readonly Dictionary<string, (PdfDocument Document, LayoutTree Tree)> _trees = new(StringComparer.Ordinal);

    [Fact]
    public void Detected_paragraphs_match_the_labelled_reading_order()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int labels = 0;
        int found = 0;
        int ordered = 0;
        int detected = 0;
        foreach ((string path, int pageNumber, bool kindsOnly, List<Label> expected) in Sections())
        {
            if (kindsOnly)
            {
                continue;
            }

            string[] paragraphs = [.. Items(Tree(path), pageNumber).Select(static item => item.Text)];
            int position = 0;
            int pageOrdered = 0;
            foreach (Label label in expected)
            {
                found += paragraphs.Any(paragraph => paragraph.StartsWith(label.Text, StringComparison.Ordinal)) ? 1 : 0;
                int at = Array.FindIndex(paragraphs, position, paragraph => paragraph.StartsWith(label.Text, StringComparison.Ordinal));
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

        double recall = (double)ordered / labels;
        double precision = (double)ordered / detected;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{labels} labels: found {found} ({(double)found / labels:F2}), in order {ordered} (recall {recall:F2}), {detected} detected (precision {precision:F2})"));
        Assert.True(recall >= MinimumRecall, $"recall {recall:F2}");
        Assert.True(precision >= MinimumPrecision, $"precision {precision:F2}");
    }

    [Fact]
    public void Detected_headings_and_list_items_match_the_labelled_ones()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        // Per kind: labels, labels found as that kind, items detected as that kind, detected items matching a label.
        var tally = new SortedDictionary<string, int[]>(StringComparer.Ordinal) { ["H"] = new int[4], ["LI"] = new int[4] };
        int depths = 0;
        foreach ((string path, int pageNumber, _, List<Label> expected) in Sections())
        {
            Item[] items = Items(Tree(path), pageNumber);
            foreach ((string kind, int[] counts) in tally)
            {
                Label[] labelled = [.. expected.Where(label => label.Kind == kind)];
                Item[] found = [.. items.Where(item => item.Kind == kind)];
                counts[0] += labelled.Length;
                counts[2] += found.Length;
                foreach (Label label in labelled)
                {
                    Item[] matches = [.. found.Where(item => item.Text.StartsWith(label.Text, StringComparison.Ordinal))];
                    if (matches.Length > 0)
                    {
                        counts[1]++;
                        depths += kind == "LI" && matches[0].Depth == label.Depth ? 1 : 0;
                    }
                }

                counts[3] += found.Count(item => labelled.Any(label => item.Text.StartsWith(label.Text, StringComparison.Ordinal)));
            }
        }

        foreach ((string kind, int[] counts) in tally)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{kind}: {counts[0]} labels, {counts[1]} found (recall {(double)counts[1] / counts[0]:F2}), {counts[2]} detected (precision {(double)counts[3] / counts[2]:F2})"));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"list items found at the labelled depth: {depths}/{tally["LI"][1]}");
        Assert.True((double)tally["H"][1] / tally["H"][0] >= MinimumHeadingRecall, "heading recall");
        Assert.True((double)tally["H"][3] / tally["H"][2] >= MinimumHeadingPrecision, "heading precision");
        Assert.True((double)tally["LI"][1] / tally["LI"][0] >= MinimumListRecall, "list item recall");
        Assert.True((double)tally["LI"][3] / tally["LI"][2] >= MinimumListPrecision, "list item precision");
        Assert.Equal(tally["LI"][1], depths);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach ((PdfDocument document, _) in _trees.Values)
        {
            document.Dispose();
        }
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The page's paragraph-level items in reading order: paragraphs, headings, and list items (each before its nested lists).</summary>
    private static Item[] Items(LayoutTree tree, int pageNumber)
    {
        Assert.True(tree.Pages[pageNumber - 1].IsDetected, $"page {pageNumber} is detected");
        var items = new List<Item>();
        foreach (LayoutNode node in tree.Root.Children.Where(node => node.Extents[0].Page.PageIndex == pageNumber - 1))
        {
            Collect(node, 0, items);
        }

        return [.. items];
    }

    private static void Collect(LayoutNode node, int depth, List<Item> items)
    {
        if (node.Kind != LayoutNodeKind.List)
        {
            items.Add(new Item(Normalize(node.Text), node.Kind == LayoutNodeKind.Heading ? "H" : "P", depth));
            return;
        }

        foreach (LayoutNode item in node.Children)
        {
            items.Add(new Item(Normalize(item.Text), "LI", depth + 1));
            foreach (LayoutNode content in item.Children[^1].Children)
            {
                Collect(content, depth + 1, items);
            }
        }
    }

    private LayoutTree Tree(string path)
    {
        if (!_trees.TryGetValue(path, out (PdfDocument Document, LayoutTree Tree) entry))
        {
            PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(path));
            _trees.Add(path, entry = (document, document.ExtractLayout()));
        }

        return entry.Tree;
    }

    /// <summary>
    /// The labelled pages. A label may start with its kind: <c>[H]</c> a heading, <c>[LI]</c> a list item (<c>[LI2]</c> an item of a
    /// list nested in an item, and so on). A section headed <c>== path page kinds</c> lists only the page's headings and list items.
    /// </summary>
    private static IEnumerable<(string Path, int Page, bool KindsOnly, List<Label> Labels)> Sections()
    {
        (string Path, int Page, bool KindsOnly, List<Label> Labels)? current = null;
        foreach (string line in File.ReadLines(Path.Combine(Path.GetDirectoryName(SourcePath())!, "LayoutDetectionLabels.txt")))
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                if (current is { } done)
                {
                    yield return done;
                }

                string[] parts = line[3..].Split(' ');
                current = (parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), parts.Length > 2 && parts[2] == "kinds", []);
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                string? kind = null;
                int depth = 0;
                string text = line;
                int close = line.IndexOf("] ", StringComparison.Ordinal);
                if (line.StartsWith('[') && close > 0)
                {
                    string tag = line[1..close];
                    kind = tag.StartsWith("LI", StringComparison.Ordinal) ? "LI" : tag;
                    depth = kind != "LI" ? 0 : tag.Length > 2 ? int.Parse(tag[2..], CultureInfo.InvariantCulture) : 1;
                    text = line[(close + 2)..];
                }

                current!.Value.Labels.Add(new Label(Normalize(text), kind, depth));
            }
        }

        if (current is { } last)
        {
            yield return last;
        }
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    private readonly record struct Label(string Text, string? Kind, int Depth);

    private readonly record struct Item(string Text, string Kind, int Depth);
}
