using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Where a test opens a file from.</summary>
public enum SourceKind
{
    Path,
    Stream,
    Bytes,
}

/// <summary>Opening a well-formed file through the public document API. ISO 32000-2 §7.5.1 to §7.5.5, §7.7.2, §7.7.3.</summary>
public sealed class OpenDocumentTests
{
    public static TheoryData<string, SourceKind> AcceptanceFilesBySource => new()
    {
        { "empty-page.pdf", SourceKind.Path },
        { "empty-page.pdf", SourceKind.Stream },
        { "empty-page.pdf", SourceKind.Bytes },
        { "pdf20-header.pdf", SourceKind.Path },
        { "pdf20-header.pdf", SourceKind.Stream },
        { "pdf20-header.pdf", SourceKind.Bytes },
        { "page-tree-inherited.pdf", SourceKind.Path },
        { "page-tree-inherited.pdf", SourceKind.Stream },
        { "page-tree-inherited.pdf", SourceKind.Bytes },
    };

    [Fact]
    public void Empty_page_pdf_has_one_letter_page_and_no_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        PdfPage page = Assert.Single(document.Pages);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), page.MediaBox);
        Assert.Equal(new PdfVersion(1, 7), document.Version);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(AcceptanceFilesBySource))]
    public void A_file_opens_identically_from_a_path_a_stream_and_bytes_with_no_diagnostics(string fileName, SourceKind kind)
    {
        using PdfDocument fromPath = PdfDocument.Open(Corpus.Path(fileName));
        using PdfDocument document = Open(fileName, kind);

        Assert.Equal(DocumentProjection.Of(fromPath), DocumentProjection.Of(document));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Page_tree_inherited_pdf_pages_inherit_media_box_and_resources_from_the_intermediate_node()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));

        Assert.Equal(2, document.Pages.Count);
        var intermediate = (CosDictionary)document.Resolve(new CosReference(3, 0));
        CosObject nodeResources = intermediate[new CosName("Resources")];
        foreach (PdfPage page in document.Pages)
        {
            Assert.False(page.Dictionary.ContainsKey(new CosName("MediaBox")));
            Assert.Equal(new PdfRectangle(0, 0, 612, 792), page.MediaBox);
            Assert.Same(nodeResources, page.Resources);
        }

        Assert.Equal([new CosReference(4, 0), new CosReference(5, 0)], document.Pages.Select(static page => page.Reference));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Pdf20_header_pdf_reports_version_2_0()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdf20-header.pdf"));

        Assert.Equal(new PdfVersion(2, 0), document.Version);
        Assert.Equal("2.0", document.Version.ToString());
    }

    private static PdfDocument Open(string fileName, SourceKind kind)
    {
        switch (kind)
        {
            case SourceKind.Path:
                return PdfDocument.Open(Corpus.Path(fileName));
            case SourceKind.Stream:
                // A seekable stream is read in place, so it stays open as long as the document; a stream that is not a FileStream
                // is read under a lock rather than memory-mapped.
                return PdfDocument.Open(new ProbeStream(Corpus.Bytes(fileName)));

            default:
                return PdfDocument.Open(Corpus.Bytes(fileName));
        }
    }
}
