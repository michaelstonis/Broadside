using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The JBIG2Decode filter through its public contract (<see cref="IStreamFilter"/> and the image facet <see cref="IImageFilter"/>):
/// ISO 32000-2 §7.4.7 and ITU-T T.88 | ISO/IEC 14492.
/// </summary>
public class Jbig2DecodeFilterTests
{
    [Fact]
    public void The_MMR_generic_region_of_Annex_H_decodes_to_the_page_jbig2dec_gives()
    {
        using DecodedImage image = Jbig2Testing.DecodeImage(Jbig2Samples.AnnexHMmrGeneric, 64, 56, out string[] codes);

        Assert.Equal((64, 56, 1, 1, false), (image.Width, image.Height, image.Components, image.BitsPerComponent, image.SamplesInverted));
        Assert.Equal(Jbig2Samples.AnnexHGenericPageSha256, Jbig2Testing.Sha256(image.Samples));
        Assert.Empty(codes);
    }

    [Fact]
    public void The_arithmetic_generic_region_of_Annex_H_with_moved_AT_pixels_and_typical_prediction_decodes_to_the_same_page()
    {
        using DecodedImage image = Jbig2Testing.DecodeImage(Jbig2Samples.AnnexHArithmeticGeneric, 64, 56, out string[] codes);

        Assert.Equal(Jbig2Samples.AnnexHGenericPageSha256, Jbig2Testing.Sha256(image.Samples));
        Assert.Empty(codes);
    }

    [Fact]
    public void The_plain_filter_path_writes_the_image_samples()
    {
        (byte[] decoded, string[] codes) = Jbig2Testing.Decode(Jbig2Samples.AnnexHArithmeticGeneric, 64, 56);

        Assert.Equal(Jbig2Samples.AnnexHGenericPageSha256, Jbig2Testing.Sha256(decoded));
        Assert.Empty(codes);
    }

    public static TheoryData<int, bool, string> Templates => new()
    {
        { 0, false, "nominal" }, { 0, true, "nominal" }, { 0, false, "moved" }, { 0, true, "moved" }, { 0, true, "current-row" },
        { 1, false, "nominal" }, { 1, true, "nominal" }, { 1, false, "moved" }, { 1, true, "moved" },
        { 2, false, "nominal" }, { 2, true, "nominal" }, { 2, false, "moved" }, { 2, true, "moved" },
        { 3, false, "nominal" }, { 3, true, "nominal" }, { 3, false, "moved" }, { 3, true, "moved" },
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_template_with_or_without_typical_prediction_and_moved_AT_pixels_decodes_the_bitmap_it_was_encoded_from(int template, bool typicalPrediction, string at)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(203, 61);
        Jbig2AtPixels pixels = at switch
        {
            "moved" when template == 0 => new((-5, -3), (7, -1), (0, -4), (-128, 0)),
            "current-row" => new((3, -1), (-3, -1), (2, -2), (-1, 0)),
            "moved" => new((-6, -2)),
            _ => Jbig2AtPixels.Nominal(template),
        };
        byte[] page = Page(203, 61, Jbig2Encoder.GenericRegion(bitmap, 0, 0, 0, mmr: false, template, typicalPrediction, pixels));

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 203, 61, out string[] codes);

        Assert.Equal(Jbig2Encoder.PackPdf(bitmap, 203), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_MMR_region_decodes_with_or_without_EOFB(bool endOfBlock)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(77, 33);
        byte[] region = [.. Jbig2Encoder.GenericRegion(bitmap, 0, 0, 0, mmr: true)[..18], .. Jbig2Encoder.EncodeMmr(bitmap, endOfBlock)];

        using DecodedImage image = Jbig2Testing.DecodeImage(Page(77, 33, region), 77, 33, out string[] codes);

        Assert.Equal(Jbig2Encoder.PackPdf(bitmap, 77), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    public static TheoryData<int, int> Operators => new()
    {
        // Region operator (0 OR, 1 AND, 2 XOR, 3 XNOR, 4 REPLACE), page default pixel.
        { 0, 0 }, { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 0, 1 }, { 1, 1 }, { 2, 1 }, { 3, 1 }, { 4, 1 },
    };

    [Theory]
    [MemberData(nameof(Operators))]
    public void Regions_combine_with_the_page_by_their_operator_clipped_to_the_image(int op, int defaultPixel)
    {
        bool[][] first = CcittEncoder.SampleBitmap(40, 20, seed: 1);
        bool[][] second = CcittEncoder.SampleBitmap(40, 20, seed: 2);
        byte[] page = [
            .. Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(50, 30, defaultPixel, operatorOverridden: true)),
            .. Jbig2Encoder.Segment(1, 38, 1, Jbig2Encoder.GenericRegion(first, 3, 2, 0, mmr: false)),
            .. Jbig2Encoder.Segment(2, 38, 1, Jbig2Encoder.GenericRegion(second, 13, 15, op, mmr: true)),
        ];
        bool[][] expected = new bool[30][];
        for (int y = 0; y < 30; y++)
        {
            expected[y] = new bool[50];
            for (int x = 0; x < 50; x++)
            {
                bool pixel = defaultPixel == 1;
                if (x >= 3 && x < 43 && y >= 2 && y < 22)
                {
                    pixel |= first[y - 2][x - 3];
                }

                if (x >= 13 && y >= 15)
                {
                    bool s = second[y - 15][x - 13];
                    pixel = op switch { 0 => pixel | s, 1 => pixel & s, 2 => pixel ^ s, 3 => !(pixel ^ s), _ => s };
                }

                expected[y][x] = pixel;
            }
        }

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 50, 30, out string[] codes);

        Assert.Equal(Jbig2Encoder.PackPdf(expected, 50), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void Global_segments_come_from_the_JBIG2Globals_stream()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(30, 10);
        byte[] globals = Jbig2Encoder.Segment(0, 62, 0, Jbig2Encoder.Comment("Title", "globals"));
        byte[] page = [
            .. Jbig2Encoder.Segment(1, 48, 1, Jbig2Encoder.PageInformation(30, 10)),
            .. Jbig2Encoder.Segment(2, 62, 1, Jbig2Encoder.Comment("Page", "one")),
            .. Jbig2Encoder.Segment(3, 39, 1, Jbig2Encoder.GenericRegion(bitmap, 0, 0, 0, mmr: false, 1, true)),
            .. Jbig2Encoder.Segment(4, 50, 1, Jbig2Encoder.BigEndian(9)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 30, 10, out string[] codes, globals);

        Assert.Equal(Jbig2Encoder.PackPdf(bitmap, 30), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_region_of_unknown_length_ends_at_its_terminator_and_row_count(bool mmr)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(64, 24);
        byte[] region = Jbig2Encoder.GenericRegion(bitmap[..20], 0, 0, 0, mmr, 0, typicalPrediction: !mmr, rowCount: 20);
        BinaryPrimitivesWriteHeight(region, 24);
        byte[] page = [
            .. Jbig2Encoder.Segment(1, 48, 1, Jbig2Encoder.PageInformation(64, 0xFFFFFFFF, striping: 0x8000 | 32)),
            .. Jbig2Encoder.Segment(2, 38, 1, region, declaredLength: uint.MaxValue),
            .. Jbig2Encoder.Segment(3, 50, 1, Jbig2Encoder.BigEndian(23)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 64, 24, out string[] codes);

        bool[][] expected = [.. bitmap[..20], .. Enumerable.Range(0, 4).Select(_ => new bool[64])];
        Assert.Equal(Jbig2Encoder.PackPdf(expected, 64), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void Without_a_width_and_height_the_page_information_gives_the_size()
    {
        var filter = new Jbig2DecodeFilter();
        var context = new ImageFilterContext(new FilterContext());

        Assert.True(filter.TryReadHeader(Jbig2Samples.AnnexHMmrGeneric, context, out ImageHeader header));
        Assert.Equal(new ImageHeader(64, 56, 1, 1) { ColorModel = ImageColorModel.Gray }, header);
        using DecodedImage image = filter.DecodeImage(Jbig2Samples.AnnexHMmrGeneric, context)!;
        Assert.Equal(Jbig2Samples.AnnexHGenericPageSha256, Jbig2Testing.Sha256(image.Samples));
    }

    private static void BinaryPrimitivesWriteHeight(byte[] region, uint height) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(region.AsSpan(4), height);

    /// <summary>A page stream: page information for <paramref name="width"/> x <paramref name="height"/> and one immediate lossless generic region.</summary>
    private static byte[] Page(uint width, uint height, byte[] region) =>
        [.. Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(width, height)), .. Jbig2Encoder.Segment(1, 39, 1, region)];
}
