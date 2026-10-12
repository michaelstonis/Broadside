using System.Buffers;
using System.IO.Compression;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Saving compresses unfiltered streams with the <c>FlateDecode</c> encoder. ISO 32000-2 §7.4.4, §7.5, §7.3.8.2.</summary>
public sealed class StreamCompressionTests
{
    private static readonly CosName Filter = new("Filter");
    private static readonly CosName Contents = new("Contents");

    /// <summary>Every unencrypted well-formed corpus file (encrypted files cannot be saved yet).</summary>
    public static TheoryData<string> WellFormedFiles => new(Corpus.WellFormedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal)));

    [Fact]
    public void A_changed_stream_is_compressed_by_default_and_the_document_keeps_its_own_data()
    {
        byte[] file = Corpus.Bytes("text-standard14-widths.pdf");
        using PdfDocument source = PdfDocument.Open(file);
        CosStream contents = PageContents(source);
        byte[] data = LongContent();
        contents.EncodedData = data;

        byte[] saved = Save(source, new PdfSaveOptions());

        using PdfDocument reopened = PdfDocument.Open(saved);
        CosStream written = PageContents(reopened);
        Assert.Equal(new CosName("FlateDecode"), written.Dictionary[Filter]);
        Assert.True(written.EncodedData.Length < data.Length / 4);
        Assert.Equal(data, reopened.DecodeStream(written).ToArray());
        Assert.Empty(reopened.Diagnostics);
        Assert.False(contents.Dictionary.ContainsKey(Filter));
        Assert.Equal(data, contents.EncodedData.ToArray());
    }

    [Fact]
    public void Unchanged_streams_keep_their_bytes_by_default_and_nothing_is_compressed_without_compression()
    {
        byte[] file = Corpus.Bytes("text-type1-embedded.pdf");
        using PdfDocument source = PdfDocument.Open(file);
        CosStream contents = PageContents(source);
        contents.EncodedData = LongContent();

        byte[] byDefault = Save(source, new PdfSaveOptions());
        byte[] uncompressed = Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None));

        Dictionary<(int Number, int Generation), byte[]> before = ObjectBodies.Read(file);
        Dictionary<(int Number, int Generation), byte[]> after = ObjectBodies.Read(byDefault);
        var reference = (CosReference)source.Pages[0].Dictionary[Contents];
        (int, int) changed = (reference.ObjectNumber, reference.Generation);
        Assert.All(before.Keys.Where(key => key != changed), key => Assert.True(before[key].AsSpan().SequenceEqual(after[key]), $"Object {key} changed bytes."));
        using PdfDocument reopened = PdfDocument.Open(uncompressed);
        Assert.False(PageContents(reopened).Dictionary.ContainsKey(Filter));
        Assert.True(uncompressed.Length > byDefault.Length);
    }

    [Fact]
    public void A_document_with_uncompressed_streams_saved_with_compression_shrinks_and_passes_qpdf_check()
    {
        byte[] file = Corpus.Bytes("text-type1-embedded.pdf");
        using PdfDocument source = PdfDocument.Open(file);

        byte[] compressed = Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.AllStreams));
        byte[] uncompressed = Save(source, new PdfSaveOptions());

        Assert.True(compressed.Length < uncompressed.Length, $"Compressed {compressed.Length} bytes, uncompressed {uncompressed.Length}.");
        (int exitCode, string output) = ExternalTool.Run("qpdf", compressed, "--check");
        Assert.True(exitCode == 0, output);
        using PdfDocument reopened = PdfDocument.Open(compressed);
        AssertSameStreamData(source, reopened);
    }

    [Theory]
    [MemberData(nameof(WellFormedFiles))]
    public void Every_well_formed_file_saved_with_every_stream_compressed_decodes_to_the_same_data_and_passes_qpdf_check(string fileName)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        byte[] compressed = Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.AllStreams));

        using PdfDocument reopened = PdfDocument.Open(compressed);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity > Broadside.Diagnostics.DiagnosticSeverity.Information);
        AssertSameStreamData(source, reopened);
        Assert.True(compressed.Length <= Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None)).Length);
        (int exitCode, string output) = ExternalTool.Run("qpdf", compressed, "--check");
        Assert.True(exitCode is 0 or 3 && !output.Contains("ERROR", StringComparison.Ordinal), output);
    }

    [Fact]
    public void A_metadata_stream_is_never_compressed()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"));

        using PdfDocument reopened = PdfDocument.Open(Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.AllStreams)));

        var metadata = (CosStream)reopened.Resolve(reopened.Catalog[new CosName("Metadata")]);
        Assert.False(metadata.Dictionary.ContainsKey(Filter));
    }

    [Fact]
    public void A_changed_stream_that_compression_would_not_shrink_is_written_as_it_is()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14-widths.pdf"));
        CosStream contents = PageContents(source);
        byte[] data = Encoding.ASCII.GetBytes("q Q");
        contents.EncodedData = data;

        using PdfDocument reopened = PdfDocument.Open(Save(source, new PdfSaveOptions()));

        Assert.False(PageContents(reopened).Dictionary.ContainsKey(Filter));
        Assert.Equal(data, PageContents(reopened).EncodedData.ToArray());
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal, PdfCrossReferenceLayout.StreamWithObjectStreams)]
    [InlineData(CompressionLevel.Fastest, PdfCrossReferenceLayout.StreamWithObjectStreams)]
    [InlineData(CompressionLevel.SmallestSize, PdfCrossReferenceLayout.Stream)]
    [InlineData(CompressionLevel.NoCompression, PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void Every_compression_level_writes_a_file_that_reopens_with_the_same_streams(CompressionLevel level, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-cid-embedded-cmap.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithCompressionLevel(level).WithCrossReferenceLayout(layout).WithStreamCompression(PdfStreamCompression.AllStreams));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        AssertSameStreamData(source, reopened);
    }

    [Fact]
    public void Saving_compresses_with_the_flate_encoder_the_options_registered()
    {
        var encoder = new CountingFlateEncoder();
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14-widths.pdf"), new PdfOptions().UseStreamEncoder(encoder));
        PageContents(source).EncodedData = LongContent();

        using PdfDocument reopened = PdfDocument.Open(Save(source, new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams)));

        // The changed content stream, the object stream and the cross-reference stream.
        Assert.Equal(3, encoder.Calls);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Save_options_reject_undefined_settings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSaveOptions().WithStreamCompression((PdfStreamCompression)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSaveOptions().WithCompressionLevel((CompressionLevel)42));
        Assert.Equal(PdfStreamCompression.ChangedStreams, new PdfSaveOptions().StreamCompression);
        Assert.Equal(CompressionLevel.Optimal, new PdfSaveOptions().CompressionLevel);
    }

    private static CosStream PageContents(PdfDocument document) => (CosStream)document.Resolve(document.Pages[0].Dictionary[Contents]);

    /// <summary>A content stream long enough to compress well: the same text-showing operations many times.</summary>
    private static byte[] LongContent()
    {
        var text = new StringBuilder();
        for (int line = 0; line < 200; line++)
        {
            text.Append("BT /F1 12 Tf 72 ").Append(700 - (line % 50 * 12)).Append(" Td (Compressed by the save) Tj ET\n");
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>Every stream reachable from the source's trailer decodes, in the saved file, to the same data under the same number.</summary>
    private static void AssertSameStreamData(PdfDocument source, PdfDocument saved)
    {
        var pending = new Stack<CosObject>([source.Trailer]);
        var seen = new HashSet<CosReference>();
        while (pending.TryPop(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference when seen.Add(reference):
                    CosObject resolved = source.Resolve(reference);
                    if (resolved is CosStream original)
                    {
                        var written = Assert.IsType<CosStream>(saved.Resolve(reference));
                        Assert.Equal(source.DecodeStream(original).ToArray(), saved.DecodeStream(written).ToArray());
                    }

                    pending.Push(resolved);
                    break;
                case CosArray array:
                    foreach (CosObject item in array)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosDictionary dictionary:
                    foreach (CosObject item in dictionary.Values)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                default:
                    break;
            }
        }
    }

    private sealed class CountingFlateEncoder : IStreamEncoder
    {
        private readonly FlateEncoder _inner = new();

        public int Calls { get; private set; }

        public CosName Name => _inner.Name;

        public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
        {
            Calls++;
            _inner.Encode(data, output, context);
        }
    }
}
