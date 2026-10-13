using Broadside.Tests.Document;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// <see cref="PdfPage.ExtractText()"/> over a long real-world document allocates what it returns and little more (issue #284): the
/// one-call path a caller indexing a whole file takes. freeculture.pdf (352 pages, about 670,000 glyphs) allocated about 930 bytes
/// per glyph when every page grew a fresh extractor; its output is about 222. <c>RealWorldTextBenchmarks.ExtractTextWarm</c> is the
/// measuring half. Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(HeavyTestCollection.Name)]
public sealed class RealWorldTextAllocationTests
{
    /// <summary>What a page may allocate beyond its output: the page object, the assembled record and the content run's small objects.</summary>
    private const long PageOverhead = 1024;

    [Fact]
    public void Extracting_every_page_of_freeculture_with_its_plain_text_allocates_the_output_and_at_most_a_kilobyte_a_page()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null || !File.Exists(RealWorldCorpus.PathOf("pdfjs/freeculture.pdf")))
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf("pdfjs/freeculture.pdf")));
        long output = 0;
        long glyphs = 0;
        foreach (PdfPage page in document.Pages)
        {
            TextPage text = page.ExtractText();
            glyphs += text.Glyphs.Count;
            output += TextPageSize.Assembled(text);
        }

        long allocated = Allocations.Measure(
            () =>
            {
                foreach (PdfPage page in document.Pages)
                {
                    _ = page.ExtractText().PlainText;
                }
            },
            warmUpCalls: 2);

        Assert.True(glyphs > 600_000, $"Only {glyphs} glyphs.");
        Assert.InRange(allocated, output, output + (PageOverhead * document.Pages.Count));
    }
}
