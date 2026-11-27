using System.Text;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// The Markdown of every tagged file of <c>tests/Corpus/</c>, and of untagged real-world pages laid out by detection, snapshotted
/// (issue #629). ISO 32000-2 §14.8.4; ISO/TS 32005.
/// </summary>
public sealed class MarkdownSnapshotTests
{
    private static readonly MarkdownSaveOptions Options = new MarkdownSaveOptions().WithPageBreaks(true);

    public static TheoryData<string> TaggedFiles => [.. Corpus.WellFormedFileNames.Where(IsTagged).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(TaggedFiles))]
    public async Task A_tagged_corpus_file_writes_the_expected_markdown(string fileName)
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Path(fileName), TestContext.Current.CancellationToken);

        string markdown = document.ExtractMarkdown(Options);

        await Verify(markdown).UseParameters(fileName);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public async Task Untagged_real_world_pages_write_the_expected_markdown()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        // A two-column paper, a book page with running headers, and a form with a table (the pages LayoutDetectionLabels.txt labels).
        (string Path, int Page)[] pages = [("pdfjs/tracemonkey.pdf", 1), ("pdfjs/freeculture.pdf", 20), ("pdfjs/bug1669099.pdf", 1)];
        var output = new StringBuilder();
        foreach ((string path, int page) in pages)
        {
            byte[] bytes = await File.ReadAllBytesAsync(RealWorldCorpus.PathOf(path), TestContext.Current.CancellationToken);
            using PdfDocument document = PdfDocument.Open(bytes);
            output.Append("==== ").Append(path).Append(" page ").Append(page).Append('\n');
            output.Append(document.Pages[page - 1].ExtractMarkdown(Options)).Append('\n');
        }

        await Verify(output.ToString());
    }

    private static bool IsTagged(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        return document.StructureTree is not null;
    }
}
