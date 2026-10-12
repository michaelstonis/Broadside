using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Encoding a stream through a document: the encoders of its <c>Filter</c> entry in reverse order and the predictors of
/// <c>DecodeParms</c>, checked by decoding it again. ISO 32000-2 §7.3.8.2, §7.4.1, §7.4.4.3 and §7.4.4.4.
/// </summary>
public sealed class EncodeStreamTests
{
    public static TheoryData<int> Predictors => new() { 2, 10, 11, 12, 13, 14, 15 };

    [Theory]
    [MemberData(nameof(Predictors))]
    public void Flate_with_a_predictor_round_trips_every_colour_depth_and_width(int predictor)
    {
        using PdfDocument document = PdfDocument.Create();
        int seed = predictor;
        foreach (int colors in new[] { 1, 2, 3, 4 })
        {
            foreach (int bitsPerComponent in new[] { 1, 2, 4, 8, 16 })
            {
                foreach (int columns in new[] { 1, 5, 17 })
                {
                    int rowLength = ((colors * bitsPerComponent * columns) + 7) / 8;
                    byte[] data = StreamEncoderTests.Sample(seed % 2 == 0 ? "random" : "runs", rowLength * 9, seed++);
                    CosStream stream = Predicted(predictor, colors, bitsPerComponent, columns);

                    document.EncodeStream(stream, data);

                    Assert.Equal(data, document.DecodeStream(stream).ToArray());
                }
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    public static TheoryData<int, byte[], byte[]> PredictedRows => new()
    {
        // TIFF 2, one 8-bit colour: each byte minus the one to its left (§7.4.4.4).
        { 2, [10, 12, 15, 20, 20, 19], [10, 2, 3, 20, 0, 255] },

        // PNG Sub (11) and Up (12) on two rows of three bytes: each row starts with its tag (Table 10).
        { 11, [1, 2, 3, 4, 6, 8], [1, 1, 1, 1, 1, 4, 2, 2] },
        { 12, [1, 2, 3, 4, 6, 8], [2, 1, 2, 3, 2, 3, 4, 5] },

        // PNG optimum (15): a zero row costs nothing as None; for 5 5 5 below it, Sub (5 0 0) and Paeth (5 0 0) tie at 5 and the
        // lower tag wins, ahead of Average (5 3 3) and None and Up (15).
        { 15, [0, 0, 0, 5, 5, 5], [0, 0, 0, 0, 1, 5, 0, 0] },
    };

    [Theory]
    [MemberData(nameof(PredictedRows))]
    public void The_predictor_is_applied_before_the_flate_encoder(int predictor, byte[] data, byte[] predicted)
    {
        using PdfDocument document = PdfDocument.Create();
        CosStream stream = Predicted(predictor, colors: 1, bitsPerComponent: 8, columns: 3);

        document.EncodeStream(stream, data);

        // Inflating without the predictor shows what the predictor wrote.
        Assert.Equal(predicted, FilterTesting.Run(new FlateDecodeFilter(), stream.EncodedData.Span).Decoded);
    }

    [Fact]
    public void A_chain_runs_its_encoders_last_name_first()
    {
        // §7.4.1 EXAMPLE 2: data encoded with LZW and then ASCII base-85 is decoded by [/ASCII85Decode /LZWDecode].
        using PdfDocument document = PdfDocument.Create();
        byte[] data = FilterEncoders.SampleData(5_000);
        var stream = new CosStream(Dictionary("<< /Filter [/ASCII85Decode /FlateDecode] /DecodeParms [null << /Predictor 12 /Columns 10 >>] >>"), ReadOnlyMemory<byte>.Empty);

        document.EncodeStream(stream, data);

        Assert.EndsWith("~>", Encoding.ASCII.GetString(stream.EncodedData.Span), StringComparison.Ordinal);
        Assert.Equal(data, document.DecodeStream(stream).ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_stream_without_filters_takes_the_data_as_it_is_and_is_marked_changed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14-widths.pdf"));
        var contents = (CosStream)document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]);
        byte[] data = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 712 Td (Encoded) Tj ET");

        document.EncodeStream(contents, data);

        Assert.Equal(data, contents.EncodedData.ToArray());
        Assert.True(contents.IsDirty);
    }

    public static TheoryData<string, int> InvalidPredictedData => new()
    {
        // Seven bytes are two rows of three and one byte: a reader would pad the last row with zeros.
        { "<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 3 >> >>", 7 },
        { "<< /Filter /FlateDecode /DecodeParms << /Predictor 3 >> >>", 6 },
        { "<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /BitsPerComponent 3 >> >>", 6 },
        { "<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 0 >> >>", 6 },
        { "<< /Filter /Fl >>", 6 },
        { "<< /Filter (FlateDecode) >>", 6 },
        { "<< /Filter [/FlateDecode /ASCIIHexDecode] /DecodeParms [null] >>", 6 },
        { "<< /DecodeParms << /Predictor 12 >> >>", 6 },
    };

    [Theory]
    [MemberData(nameof(InvalidPredictedData))]
    public void A_dictionary_the_encoders_cannot_honour_is_rejected_and_the_data_is_left_alone(string dictionary, int length)
    {
        using PdfDocument document = PdfDocument.Create();
        var stream = new CosStream(Dictionary(dictionary), new byte[] { 1, 2, 3 });

        Assert.Throws<ArgumentException>(() => document.EncodeStream(stream, new byte[length]));
        Assert.Equal([1, 2, 3], stream.EncodedData.ToArray());
    }

    [Theory]
    [InlineData("Crypt")]
    [InlineData("LZWDecode")]
    [InlineData("Unknown")]
    public void A_filter_without_an_encoder_is_not_supported(string name)
    {
        using PdfDocument document = PdfDocument.Create();
        var stream = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName(name) }, ReadOnlyMemory<byte>.Empty);

        Assert.Throws<NotSupportedException>(() => document.EncodeStream(stream, new byte[8]));
    }

    [Fact]
    public void A_DCT_image_stream_is_encoded_by_the_default_DCT_encoder_from_its_image_dictionary()
    {
        // §7.4.8: the default encoder writes a baseline JPEG at quality 75 with 4:2:0 chroma, which the DCT filter decodes to
        // samples of the image's size and close to the input.
        using PdfDocument document = PdfDocument.Create();
        Dct.DctGolden image = Dct.DctVectors.Golden("rgb");
        CosDictionary dictionary = Dct.DctEncoderTests.ImageDictionary(image);
        dictionary[new CosName("Filter")] = new CosName("DCTDecode");
        var stream = new CosStream(dictionary, ReadOnlyMemory<byte>.Empty);

        document.EncodeStream(stream, image.Samples);

        Assert.Equal(Dct.DctVectors.Jpeg("encoded-rgb-q75-420"), stream.EncodedData.ToArray());
        byte[] decoded = document.DecodeStream(stream).ToArray();
        Assert.True(Dct.DctEncoderTests.Psnr(image.Samples, decoded) > 30);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_registered_DCT_encoder_sets_the_quality_and_subsampling_of_the_documents_DCT_streams()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStreamEncoder(new DctEncoder(90, DctChromaSubsampling.None)));
        Dct.DctGolden image = Dct.DctVectors.Golden("rgb");
        CosDictionary dictionary = Dct.DctEncoderTests.ImageDictionary(image);
        dictionary[new CosName("Filter")] = new CosName("DCTDecode");
        var stream = new CosStream(dictionary, ReadOnlyMemory<byte>.Empty);

        document.EncodeStream(stream, image.Samples);

        Assert.Equal(Dct.DctVectors.Jpeg("encoded-rgb-q90-444"), stream.EncodedData.ToArray());
    }

    [Fact]
    public void A_DCT_stream_without_an_image_dictionary_is_rejected()
    {
        using PdfDocument document = PdfDocument.Create();
        var stream = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("DCTDecode") }, new byte[] { 1, 2, 3 });

        Assert.Throws<ArgumentException>(() => document.EncodeStream(stream, new byte[8]));
        Assert.Equal([1, 2, 3], stream.EncodedData.ToArray());
    }

    [Fact]
    public void A_registered_encoder_replaces_the_default_of_its_name_and_a_new_name_becomes_encodable()
    {
        var flate = new CountingEncoder(new FlateEncoder());
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStreamEncoder(flate).UseStreamEncoder(new LzwEncoder()));
        byte[] data = FilterEncoders.SampleData(3_000);
        var deflated = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("FlateDecode") }, ReadOnlyMemory<byte>.Empty);
        var lzw = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("LZWDecode") }, ReadOnlyMemory<byte>.Empty);

        document.EncodeStream(deflated, data);
        document.EncodeStream(lzw, data);

        Assert.Equal(1, flate.Calls);
        Assert.Equal(data, document.DecodeStream(deflated).ToArray());
        Assert.Equal(data, document.DecodeStream(lzw).ToArray());
    }

    [Theory]
    [InlineData("Crypt")]
    [InlineData("Fl")]
    [InlineData("AHx")]
    public void An_encoder_cannot_be_registered_for_crypt_or_an_abbreviation(string name)
    {
        Assert.Throws<ArgumentException>(() => new PdfOptions().UseStreamEncoder(new NamedEncoder(new CosName(name))));
    }

    private static CosStream Predicted(int predictor, int colors, int bitsPerComponent, int columns) => new(
        Dictionary($"<< /Filter /FlateDecode /DecodeParms << /Predictor {predictor} /Colors {colors} /BitsPerComponent {bitsPerComponent} /Columns {columns} >> >>"),
        ReadOnlyMemory<byte>.Empty);

    private static CosDictionary Dictionary(string text) => (CosDictionary)CosObject.Parse(Encoding.ASCII.GetBytes(text));

    /// <summary>Wraps an encoder and counts its calls.</summary>
    private sealed class CountingEncoder(IStreamEncoder inner) : IStreamEncoder
    {
        public int Calls { get; private set; }

        public CosName Name => inner.Name;

        public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
        {
            Calls++;
            inner.Encode(data, output, context);
        }
    }

    /// <summary>An LZW encoder for the test only: the port of the corpus generator's encoder.</summary>
    private sealed class LzwEncoder : IStreamEncoder
    {
        public CosName Name => new("LZWDecode");

        public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context) =>
            output.Write(FilterEncoders.LzwEncode(data.ToArray()));
    }

    private sealed class NamedEncoder(CosName name) : IStreamEncoder
    {
        public CosName Name => name;

        public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context) => output.Write(data);
    }
}
