using System.Globalization;
using System.Text;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// The layout trees of the tagged PDF 2.0 files of the fetched corpora (the PDF Association's <c>pdf20examples</c>, the passing
/// files of <c>verapdf-corpus</c> and pdf.js's tagged test files), snapshotted, and checked for consistency: every extent's ranges lie
/// within its page and inside its parent's. ISO 32000-2 §14.8.4; ISO/TS 32005. Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldLayoutTreeTests
{
    private const int MaxNodes = 40;
    private const int MaxText = 60;

    private static readonly string[] CorpusIds = ["pdf20examples", "verapdf-corpus", "pdfjs"];

    [Fact]
    public async Task Tagged_pdf_2_0_files_yield_layout_trees_matching_the_snapshot()
    {
        if (CorpusIds.Any(static id => RealWorldCorpus.Directory(id) is null))
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        IEnumerable<string> candidates = CorpusIds.SelectMany(RealWorldCorpus.Files)
            .Where(static path => !path.StartsWith("verapdf-corpus/", StringComparison.Ordinal) || path.Contains("-pass-", StringComparison.Ordinal));
        var output = new StringBuilder();
        int files = 0;
        int nodes = 0;
        foreach (string path in candidates)
        {
            byte[] bytes = await File.ReadAllBytesAsync(RealWorldCorpus.PathOf(path), TestContext.Current.CancellationToken);
            using PdfDocument? document = TryOpen(bytes);
            if (document?.StructureTree is null || document.Version.Major < 2)
            {
                continue;
            }

            LayoutTree tree = document.ExtractLayout();
            files++;
            nodes += Check(tree.Root);
            output.Append(CultureInfo.InvariantCulture, $"{path}\n").Append(LayoutProjection.Of(tree, MaxNodes, MaxText)).Append('\n');
        }

        output.Insert(0, string.Create(CultureInfo.InvariantCulture, $"{files} tagged PDF 2.0 files, {nodes} layout nodes\n\n"));
        await Verify(output.ToString());
    }

    /// <summary>Checks a node and its descendants; returns how many there are.</summary>
    private static int Check(LayoutNode node)
    {
        int count = 1;
        foreach (LayoutExtent extent in node.Extents)
        {
            Assert.InRange(extent.GlyphStart, 0, extent.Page.Text.Glyphs.Count);
            Assert.InRange(extent.GlyphStart + extent.GlyphCount, extent.GlyphStart, extent.Page.Text.Glyphs.Count);
            Assert.InRange(extent.TextStart + extent.TextLength, extent.TextStart, extent.Page.Text.Text.Length);
            if (node.Parent is { } parent)
            {
                LayoutExtent outer = Assert.Single(parent.Extents, candidate => ReferenceEquals(candidate.Page, extent.Page));
                Assert.InRange(extent.GlyphStart, outer.GlyphStart, outer.GlyphStart + outer.GlyphCount);
                Assert.InRange(extent.GlyphStart + extent.GlyphCount, extent.GlyphStart, outer.GlyphStart + outer.GlyphCount);
            }
        }

        foreach (LayoutNode child in node.Children)
        {
            Assert.Same(node, child.Parent);
            count += Check(child);
        }

        return count;
    }

    private static PdfDocument? TryOpen(byte[] bytes)
    {
        try
        {
            return PdfDocument.Open(bytes);
        }
        catch (Exception exception) when (exception is Diagnostics.DiagnosticException or Broadside.Security.PdfPasswordException)
        {
            return null;
        }
    }
}
