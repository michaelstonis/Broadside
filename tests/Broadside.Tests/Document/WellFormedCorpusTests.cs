using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Every well-formed corpus file opens with zero diagnostics (tests/Corpus/README.md, "Well-formed files"; ADR 0005): a diagnostic
/// means a "shall" was violated or data was repaired, never that a "should" was not followed. ISO 32000-2 §7.5, §7.7.
/// </summary>
public class WellFormedCorpusTests
{
    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void A_well_formed_file_opens_and_walks_its_pages_with_no_diagnostics(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        int expectedPages = fileName is "page-tree-inherited.pdf" or "linearized.pdf" or "linearized-xref-stream.pdf" ? 2 : 1;
        Assert.Equal(expectedPages, document.Pages.Count);
        foreach (PdfPage page in document.Pages)
        {
            Assert.True(page.MediaBox.Width > 0 && page.MediaBox.Height > 0);
            Assert.NotNull(page.Resources);
        }

        Assert.Empty(document.Diagnostics);
    }
}
