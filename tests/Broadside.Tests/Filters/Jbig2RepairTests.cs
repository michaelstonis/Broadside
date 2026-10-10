using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;
using static Broadside.TestSupport.Jbig2Encoder;

namespace Broadside.Tests.Filters;

/// <summary>
/// JBIG2Decode on streams that break ISO 32000-2 §7.4.7 or ITU-T T.88 the ways real producers do: lenient mode decodes what it can
/// and records each deviation once; strict mode throws on the first; features not decoded yet decline the image with Information.
/// </summary>
public class Jbig2RepairTests
{
    private static readonly bool[][] Bitmap = CcittEncoder.SampleBitmap(40, 16);

    private static byte[] Info(uint page = 1, uint width = 40, uint height = 16) => Segment(0, 48, page, PageInformation(width, height));

    private static byte[] Region(uint number = 1, uint page = 1, int type = 39, int op = 0, bool mmr = false) =>
        Segment(number, type, page, GenericRegion(Bitmap, 0, 0, op, mmr));

    [Fact]
    public void A_file_header_is_skipped_with_a_diagnostic_in_either_organisation()
    {
        byte[] sequential = [.. FileHeader(sequential: true), .. Info(), .. Region()];
        byte[] info = Info();
        byte[] region = Region();
        byte[] randomAccess = [.. FileHeader(sequential: false), .. info[..^19], .. region[..11], .. Segment(2, 51, 0, []), .. info[^19..], .. region[11..]];

        foreach ((byte[] page, string[] expected) in new[] { (sequential, new[] { "Jbig2FileHeaderPresent" }), (randomAccess, ["Jbig2FileHeaderPresent", "Jbig2EndOfPagePresent"]) })
        {
            using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 16, out string[] codes);
            Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
            Assert.Equal(expected, codes);
        }
    }

    [Fact]
    public void Decoding_stops_at_an_end_of_page_segment()
    {
        byte[] page = [.. Info(), .. Segment(1, 49, 1, []), .. Region(2)];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 16, out string[] codes);

        Assert.All(image.Samples.ToArray(), value => Assert.Equal(0xFF, value));
        Assert.Equal(["Jbig2EndOfPagePresent"], codes);
    }

    [Fact]
    public void A_page_other_than_1_is_decoded_and_segments_of_further_pages_are_ignored()
    {
        byte[] page = [.. Info(page: 3), .. Region(page: 3), .. Segment(2, 39, 4, GenericRegion(Bitmap, 0, 0, 2, mmr: true))];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 16, out string[] codes);

        Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2PageNumberNotOne", "Jbig2PageNumberNotOne"], codes);
    }

    [Fact]
    public void A_truncated_region_keeps_what_its_data_decodes()
    {
        byte[] whole = [.. Info(), .. Region(mmr: true)];

        using DecodedImage image = Jbig2Testing.DecodeImage(whole[..^20], 40, 16, out string[] codes);

        Assert.Equal(PackPdf(Bitmap, 40)[..20], image.Samples[..20].ToArray());
        Assert.Contains("Jbig2SegmentTruncated", codes);
    }

    [Fact]
    public void A_reserved_segment_type_such_as_the_colour_palette_is_skipped()
    {
        byte[] page = [.. Info(), .. Segment(1, 54, 1, [1, 2, 3]), .. Region(2)];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 16, out string[] codes);

        Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2SegmentTypeReserved"], codes);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(22)]
    [InlineData(42)]
    public void A_region_type_not_decoded_yet_declines_the_image_with_information_even_in_strict_mode(int type)
    {
        byte[] page = [.. Info(), .. Region(), .. Segment(2, type, 1, new byte[24])];

        DecodedImage? image = Jbig2Testing.TryDecodeImage(page, 40, 16, out string[] codes, mode: PdfReadingMode.Strict);

        Assert.Null(image);
        Assert.Equal(["Jbig2UnsupportedFeature"], codes);
    }

    [Fact]
    public void Extended_templates_decline_the_image_with_information()
    {
        byte[] region = GenericRegion(Bitmap, 0, 0, 0, mmr: false);
        region[17] |= 0x10;

        DecodedImage? image = Jbig2Testing.TryDecodeImage([.. Info(), .. Segment(1, 39, 1, region)], 40, 16, out string[] codes);

        Assert.Null(image);
        Assert.Equal(["Jbig2UnsupportedFeature"], codes);
    }

    [Fact]
    public void A_page_size_that_differs_from_the_image_is_reported_and_the_image_size_wins()
    {
        using DecodedImage image = Jbig2Testing.DecodeImage([.. Info(width: 80, height: 8), .. Region()], 40, 16, out string[] codes);

        Assert.Equal((40, 16), (image.Width, image.Height));
        Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2PageSizeMismatch"], codes);
    }

    [Fact]
    public void A_region_operator_the_page_does_not_allow_is_reported_and_applied()
    {
        // The page default is OR without the override bit; the region asks for XOR over a black page (default pixel 1). T.88 §8.2
        // step 5 a) combines with the region's operator, as jbig2dec and PDFium do.
        byte[] page = [.. Segment(0, 48, 1, PageInformation(40, 16, defaultPixel: 1)), .. Region(op: 2)];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 16, out string[] codes);

        Assert.Equal(PackPdf([.. Bitmap.Select(row => row.Select(black => !black).ToArray())], 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2CombinationOperatorMismatch"], codes);
    }

    [Fact]
    public void An_intermediate_region_nothing_refines_is_dropped_with_a_diagnostic()
    {
        using DecodedImage image = Jbig2Testing.DecodeImage([.. Info(), .. Region(type: 36)], 40, 16, out string[] codes);

        Assert.All(image.Samples.ToArray(), value => Assert.Equal(0xFF, value));
        Assert.Equal(["Jbig2IntermediateRegionUnused"], codes);
    }

    [Fact]
    public void A_region_before_the_page_information_is_drawn_on_a_white_page()
    {
        using DecodedImage image = Jbig2Testing.DecodeImage(Region(), 40, 16, out string[] codes);

        Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2PageInformationMissing"], codes);
    }

    [Fact]
    public void A_region_larger_than_the_image_limits_is_not_decoded()
    {
        byte[] region = GenericRegion(Bitmap, 0, 0, 0, mmr: false);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(region, 1_000_000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(region.AsSpan(4), 1_000_000);

        using DecodedImage image = Jbig2Testing.DecodeImage([.. Info(), .. Segment(1, 39, 1, region)], 40, 16, out string[] codes);

        Assert.Equal(["Jbig2LimitExceeded"], codes);
    }

    [Fact]
    public void An_unparseable_segment_header_ends_the_stream()
    {
        byte[] bad = [0, 0, 0, 2, 39, 0xA0, 1, 0, 0, 0, 0];

        using DecodedImage image = Jbig2Testing.DecodeImage([.. Info(), .. Region(), .. bad], 40, 16, out string[] codes);

        Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2SegmentHeaderInvalid"], codes);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(13)]
    public void A_segment_header_cut_inside_its_page_association_or_length_ends_the_stream(int kept)
    {
        // A fuzzing finding (filter-jbig2): a segment numbered above 65536 (4-byte referred-to numbers), no referred-to segments, a
        // 4-byte page association, cut short a few bytes before the end of its data length.
        byte[] header = [0, 0x10, 0, 0, 0x40 | 39, 0, 0, 0, 0, 1, 0, 0, 0, 30];

        using DecodedImage image = Jbig2Testing.DecodeImage([.. Info(), .. header[..kept]], 40, 16, out string[] codes);

        Assert.Equal(["Jbig2SegmentTruncated"], codes);
    }

    [Fact]
    public void An_AT_pixel_below_the_current_row_reads_0()
    {
        // A fuzzing finding (filter-jbig2): A1 at (0, 100), below the last row of the region, read as 0 like any pixel outside it.
        Jbig2AtPixels at = new((0, 100), (-3, -1), (2, -2), (-2, -2));
        byte[] region = GenericRegion(Bitmap, 0, 0, 0, mmr: false, at: at);

        using DecodedImage image = Jbig2Testing.DecodeImage([.. Info(), .. Segment(1, 39, 1, region)], 40, 16, out string[] codes);

        Assert.Equal(PackPdf(Bitmap, 40), image.Samples.ToArray());
        Assert.Equal(["Jbig2SegmentInvalid"], codes);
    }

    [Fact]
    public void A_JBIG2Globals_entry_that_is_not_a_stream_is_ignored()
    {
        var context = new FilterContext { Parameters = new CosDictionary { [new CosName("JBIG2Globals")] = new CosInteger(7) } };

        using DecodedImage image = new Jbig2DecodeFilter().DecodeImage(Jbig2Samples.AnnexHMmrGeneric, new ImageFilterContext(context) { Width = 64, Height = 56 })!;

        Assert.Equal(Jbig2Samples.AnnexHGenericPageSha256, Jbig2Testing.Sha256(image.Samples));
        Assert.Equal(["Jbig2GlobalsInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Strict_mode_throws_on_the_first_deviation()
    {
        byte[] page = [.. FileHeader(), .. Info(), .. Region()];

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => Jbig2Testing.TryDecodeImage(page, 40, 16, out _, mode: PdfReadingMode.Strict));

        Assert.Equal("Jbig2FileHeaderPresent", exception.Diagnostic.Code);
    }
}
