using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// CCITT fax images through <see cref="PdfImage.Decode"/>: the codec's image facet, the image's Width and Height against Columns and
/// Rows, and polarity with image masks. ISO 32000-2 §7.4.6, §8.9.5, §8.9.6.2.
/// </summary>
public class CcittImageTests
{
    public static TheoryData<string, int, int> CorpusFiles => new()
    {
        { "ccitt-g3-1d.pdf", 203, 40 },
        { "ccitt-g3-1d-eol-align.pdf", 150, 30 },
        { "ccitt-g3-2d.pdf", 2600, 30 },
        { "ccitt-g4.pdf", 1728, 40 },
        { "ccitt-g4-no-eob.pdf", 120, 40 },
        { "ccitt-g4-align.pdf", 77, 24 },
    };

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_corpus_fax_image_decodes_to_the_bitmap_it_was_generated_from(string file, int width, int height)
    {
        // The expected samples are also what Ghostscript's CCITTFaxDecode filter gives for these streams (tests/Corpus/README.md).
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((width, height, 1, 1), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent));
        Assert.Equal(CcittEncoder.Pack(CcittEncoder.SampleBitmap(width, height)), decoded.Samples.ToArray());
        Assert.Equal(height, decoded.DecodedRows);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_corpus_stencil_masks_paint_the_black_pixels_with_BlackIs1_and_Decode_1_0_as_with_the_defaults()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("ccitt-blackis1-mask.pdf"));
        bool[][] bitmap = CcittEncoder.SampleBitmap(48, 16);

        foreach ((string name, bool blackIs1) in new[] { ("Im0", true), ("Im1", false) })
        {
            PdfImage image = document.Pages[0].GetImage(name)!;
            using DecodedImage decoded = image.Decode()!;
            Assert.True(image.IsStencil);
            Assert.Equal(CcittEncoder.Pack(bitmap, blackIs1), decoded.Samples.ToArray());
            byte[] coverage = new byte[48];
            for (int y = 0; y < 16; y++)
            {
                ImageRows.Stencil(decoded.GetRow(y), 48, image.CreateDecodeMap(decoded).IsInverted, coverage);
                Assert.Equal(bitmap[y].Select(black => black ? (byte)255 : (byte)0), coverage);
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_corpus_inline_image_decodes_through_the_CCF_abbreviation()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(File.ReadAllBytes(Corpus.Path("ccitt-inline.pdf")));
        using (document)
        {
            using DecodedImage decoded = Assert.Single(collected.Images).Decode()!;

            Assert.Equal(CcittEncoder.Pack(CcittEncoder.SampleBitmap(64, 16)), decoded.Samples.ToArray());
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void The_damaged_corpus_row_is_replaced_by_the_row_above_within_DamagedRowsBeforeError()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("ccitt-g3-damaged.pdf"));
        bool[][] expected = CcittEncoder.SampleBitmap(96, 24);
        expected[5] = expected[4];

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(CcittEncoder.Pack(expected), decoded.Samples.ToArray());
        Assert.Equal(["CcittDamagedRowReplaced"], Codes(document));
        using PdfDocument strict = PdfDocument.Open(Corpus.Path("ccitt-g3-damaged.pdf"), new PdfOptions().UseStrict());
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => strict.Pages[0].GetImage("Im0")!.Decode());
        Assert.Equal("CcittDamagedRowReplaced", error.Diagnostic.Code);
    }

    [Fact]
    public void The_truncated_corpus_image_keeps_its_complete_rows_and_is_white_after_the_last_code()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("ccitt-g4-truncated.pdf"));
        byte[] expected = CcittEncoder.Pack(CcittEncoder.SampleBitmap(64, 32));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        byte[] samples = decoded.Samples.ToArray();
        Assert.Equal(expected[..(20 * 8)], samples[..(20 * 8)]);
        Assert.All(samples[(21 * 8)..], value => Assert.Equal(0xFF, value));
        Assert.Equal(21, decoded.DecodedRows);
        Assert.Equal(["FilterDataTruncated"], Codes(document));
    }

    [Fact]
    public void A_Group_4_image_decodes_into_a_1_bit_gray_image_with_the_PDF_polarity()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(37, 19);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1));
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 37 /Height 19 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 37 >>",
            Bytes(encoded)));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal((37, 19, 1, 1, 5), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent, decoded.Stride));
        Assert.Equal(CcittEncoder.Pack(bitmap), decoded.Samples.ToArray());
        Assert.False(decoded.SamplesInverted);
        Assert.Equal(19, decoded.DecodedRows);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    public void Rows_are_decoded_at_Columns_and_cut_or_padded_with_white_to_the_image_Width(int width)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(37, 6);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: 0));
        using PdfDocument document = PdfDocument.Open(OneImage(
            $"/Width {width} /Height 6 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCF /DecodeParms << /Columns 37 >>",
            Bytes(encoded)));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        bool[][] expected = [.. bitmap.Select(row => Enumerable.Range(0, width).Select(x => x < 37 && row[x]).ToArray())];
        Assert.Equal((width, 6), (decoded.Width, decoded.Height));
        Assert.Equal(CcittEncoder.Pack(expected), decoded.Samples.ToArray());
        Assert.Contains("CcittWidthMismatch", Codes(document));
    }

    [Fact]
    public void Rows_the_data_does_not_reach_are_white_and_counted_out_of_DecodedRows()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(16, 3);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1));
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 16 /Height 5 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 16 >>",
            Bytes(encoded)));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal([.. CcittEncoder.Pack(bitmap), 0xFF, 0xFF, 0xFF, 0xFF], decoded.Samples.ToArray());
        Assert.Equal(3, decoded.DecodedRows);
        Assert.Equal(["FilterDataTruncated"], Codes(document));
    }

    [Fact]
    public void The_image_Height_caps_the_rows_decoded()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(16, 8);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1));
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 16 /Height 4 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 16 >>",
            Bytes(encoded)));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(CcittEncoder.Pack(bitmap[..4]), decoded.Samples.ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_BlackIs1_stencil_mask_with_Decode_1_0_paints_the_black_pixels()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(24, 8);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1));
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 24 /Height 8 /ImageMask true /Decode [1 0] /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 24 /BlackIs1 true >>",
            Bytes(encoded)));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;
        byte[] coverage = new byte[24];
        ImageRows.Stencil(decoded.GetRow(3), 24, image.CreateDecodeMap(decoded).IsInverted, coverage);

        Assert.Equal(CcittEncoder.Pack(bitmap, blackIs1: true), decoded.Samples.ToArray());
        Assert.Equal(bitmap[3].Select(black => black ? (byte)255 : (byte)0), coverage);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_inline_image_with_the_CCF_abbreviation_decodes()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(64, 4);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1, EndOfBlock: false));
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build(
            $"q BI /W 64 /H 4 /IM true /F /CCF /DP << /K -1 /Columns 64 /Rows 4 /EndOfBlock false >> /L {encoded.Length} ID {Bytes(encoded)} EI Q"));
        using (document)
        {
            using DecodedImage decoded = Assert.Single(collected.Images).Decode()!;

            Assert.Equal(CcittEncoder.Pack(bitmap), decoded.Samples.ToArray());
            Assert.Equal(["q", "BI", "Q"], collected.Operators);
        }
    }
}
