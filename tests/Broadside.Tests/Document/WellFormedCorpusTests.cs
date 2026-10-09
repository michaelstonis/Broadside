using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Every well-formed corpus file opens with zero diagnostics (tests/Corpus/README.md, "Well-formed files"; ADR 0005): a diagnostic
/// means a "shall" was violated or data was repaired, never that a "should" was not followed. ISO 32000-2 §7.5, §7.7.
/// </summary>
public class WellFormedCorpusTests
{
    /// <summary>Files whose only cross-reference section is a stream; issue #39 reads them and removes this list.</summary>
    private static readonly string[] CrossReferenceStreamFiles = ["xref-stream.pdf", "object-stream.pdf", "png-predictor.pdf"];

    internal static bool NeedsCrossReferenceStreams(string fileName) => CrossReferenceStreamFiles.Contains(fileName);

    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void A_well_formed_file_opens_and_walks_its_pages_with_no_diagnostics(string fileName)
    {
        Assert.SkipWhen(NeedsCrossReferenceStreams(fileName), "Needs cross-reference streams (#39).");

        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        int expectedPages = fileName is "page-tree-inherited.pdf" or "linearized.pdf" ? 2 : 1;
        Assert.Equal(expectedPages, document.Pages.Count);
        foreach (PdfPage page in document.Pages)
        {
            Assert.True(page.MediaBox.Width > 0 && page.MediaBox.Height > 0);
            Assert.NotNull(page.Resources);
        }

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("xref-stream.pdf")]
    [InlineData("object-stream.pdf")]
    [InlineData("png-predictor.pdf")]
    public void A_file_with_only_a_cross_reference_stream_is_not_readable_yet(string fileName)
    {
        // Pins the boundary of this version: issue #39 turns this into a successful open and deletes the test.
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Corpus.Path(fileName)));

        Assert.Equal("XrefStreamUnsupported", error.Diagnostic.Code);
    }
}
