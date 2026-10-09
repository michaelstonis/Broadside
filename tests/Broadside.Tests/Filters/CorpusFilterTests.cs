using System.Text;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The filter corpus files decode, through the public document API, to exactly the content stream <c>tests/Corpus/generate.py</c>
/// encoded (its <c>gen_*</c> functions; <c>pdftotext</c> prints the text each one shows). ISO 32000-2 §7.4.
/// </summary>
public class CorpusFilterTests
{
    public static TheoryData<string, string> FilteredContentStreams => new()
    {
        { "flate-stream.pdf", "BT /F1 24 Tf 72 700 Td (FlateDecode) Tj ET" },
        { "lzw-stream.pdf", "BT /F1 24 Tf 72 700 Td (LZWDecode) Tj ET" },
        { "ascii85-stream.pdf", "BT /F1 24 Tf 72 700 Td (ASCII85Decode) Tj ET" },
        { "asciihex-stream.pdf", "BT /F1 24 Tf 72 700 Td (ASCIIHexDecode) Tj ET" },
        { "runlength-stream.pdf", "BT /F1 24 Tf 72 700 Td (RunLengthDecode) Tj ET          " },
        { "filter-chain.pdf", "BT /F1 24 Tf 72 700 Td (ASCII85 then Flate) Tj ET" },
    };

    [Theory]
    [MemberData(nameof(FilteredContentStreams))]
    public void A_filtered_content_stream_decodes_to_the_bytes_that_were_encoded(string fileName, string expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        CosStream contents = ContentStream(document);

        ReadOnlyMemory<byte> decoded = document.DecodeStream(contents);

        Assert.Equal(expected, Encoding.Latin1.GetString(decoded.Span));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(FilteredContentStreams))]
    public void Decoding_into_a_buffer_writer_gives_the_same_bytes(string fileName, string expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        var output = new System.Buffers.ArrayBufferWriter<byte>();

        document.DecodeStream(ContentStream(document), output);

        Assert.Equal(expected, Encoding.Latin1.GetString(output.WrittenSpan));
    }

    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void Every_page_content_stream_of_a_well_formed_file_decodes_with_no_diagnostics(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        foreach (PdfPage page in document.Pages)
        {
            if (document.Resolve(page.Dictionary.TryGetValue(new CosName("Contents"), out CosObject? contents) ? contents : null) is CosStream stream)
            {
                Assert.False(document.DecodeStream(stream).IsEmpty);
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_unfiltered_stream_decodes_to_its_encoded_data()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        ReadOnlyMemory<byte> decoded = document.DecodeStream(ContentStream(document));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (Hello, Broadside) Tj ET", Encoding.Latin1.GetString(decoded.Span));
    }

    internal static CosStream ContentStream(PdfDocument document) =>
        Assert.IsType<CosStream>(document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]));
}
