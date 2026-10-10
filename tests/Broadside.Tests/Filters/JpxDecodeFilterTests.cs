using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The JPXDecode filter through its public contract (<see cref="IStreamFilter"/> and the image facet <see cref="IImageFilter"/>):
/// ISO 32000-2 §7.4.9 and ITU-T T.800 | ISO/IEC 15444-1.
/// </summary>
public sealed class JpxDecodeFilterTests
{
    public static TheoryData<string> LosslessVectors => new(JpxSamples.Vectors.Where(v => v.IsLossless).Select(v => v.Name));

    public static TheoryData<string> LossyVectors => new(JpxSamples.Vectors.Where(v => v.Reference is not null).Select(v => v.Name));

    [Fact]
    public void The_worked_example_of_T800_J11_decodes_to_the_samples_it_lists()
    {
        using DecodedImage image = JpxTesting.DecodeImage(JpxSamples.WorkedExample, out string[] codes);

        Assert.Equal((1, 9, 1, 8), (image.Width, image.Height, image.Components, image.BitsPerComponent));
        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Theory]
    [MemberData(nameof(LosslessVectors))]
    public void A_lossless_OpenJPEG_codestream_decodes_to_the_samples_it_was_encoded_from(string name)
    {
        JpxVector vector = JpxSamples.Vector(name);

        using DecodedImage image = JpxTesting.DecodeImage(vector.Data, out string[] codes);

        Assert.Equal((vector.Width, vector.Height, vector.Components, vector.Depth), (image.Width, image.Height, image.Components, image.BitsPerComponent));
        Assert.Equal(vector.Expected(), JpxTesting.Raw(image));
        Assert.Empty(codes);
    }

    [Fact]
    public void The_plain_filter_path_writes_the_image_samples_in_the_pdf_layout()
    {
        JpxVector vector = JpxSamples.Vector("Lrcp3LayersRct");

        (byte[] decoded, string[] codes) = JpxTesting.Decode(vector.Data);

        Assert.Equal(vector.Expected().Select(value => (byte)value), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void A_truncated_codestream_keeps_what_it_holds_with_a_diagnostic_in_lenient_mode()
    {
        byte[] truncated = JpxSamples.WorkedExample[..^4];

        using DecodedImage image = JpxTesting.DecodeImage(truncated, out string[] codes);

        Assert.Equal(["JpxCodestreamTruncated"], codes);
        Assert.Equal(9, image.Height);
        Assert.Equal(JpxSamples.WorkedExampleSamples.Length, image.Samples.Length);
    }

    [Fact]
    public void A_truncated_codestream_throws_in_strict_mode()
    {
        byte[] truncated = JpxSamples.WorkedExample[..^4];

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => JpxTesting.TryDecodeImage(truncated, out _, mode: PdfReadingMode.Strict));

        Assert.Equal("JpxCodestreamTruncated", exception.Diagnostic.Code);
    }

    [Theory]
    [MemberData(nameof(LossyVectors))]
    public void An_irreversible_OpenJPEG_codestream_decodes_within_the_conformance_tolerance_of_OpenJPEG(string name)
    {
        JpxVector vector = JpxSamples.Vector(name);

        using DecodedImage image = JpxTesting.DecodeImage(vector.Data, out string[] codes);

        Assert.Equal((vector.Width, vector.Height, vector.Components, 8), (image.Width, image.Height, image.Components, image.BitsPerComponent));
        JpxTesting.AssertWithinTolerance(vector.ReferenceSamples(), JpxTesting.Raw(image), vector.Components, peak: 4, meanSquare: 1);
        Assert.Empty(codes);
    }

    [Fact]
    public void Packet_headers_packed_in_a_PPT_marker_segment_decode_to_the_same_samples()
    {
        JpxVector vector = JpxSamples.Vector("SopEph");

        using DecodedImage image = JpxTesting.DecodeImage(JpxEditing.WithTilePacketHeaders(vector.Data), out string[] codes);

        Assert.Equal(vector.Expected(), JpxTesting.Raw(image));
        Assert.Empty(codes);
    }

    [Fact]
    public void Packet_headers_packed_in_a_PPM_marker_segment_decode_to_the_same_samples()
    {
        JpxVector vector = JpxSamples.Vector("SopEph");

        using DecodedImage image = JpxTesting.DecodeImage(JpxEditing.WithMainPacketHeaders(vector.Data), out string[] codes);

        Assert.Equal(vector.Expected(), JpxTesting.Raw(image));
        Assert.Empty(codes);
    }

    [Fact]
    public void Tile_parts_out_of_TPsot_order_are_read_in_that_order_with_a_diagnostic()
    {
        JpxVector vector = JpxSamples.Vector("TileParts");
        var parts = JpxEditing.TileParts(vector.Data);
        int second = parts.FindIndex(1, p => p.Tile == parts[0].Tile);
        int[] order = [.. Enumerable.Range(0, parts.Count).Select(i => i == 0 ? second : i == second ? 0 : i)];

        using DecodedImage image = JpxTesting.DecodeImage(JpxEditing.ReorderTileParts(vector.Data, order), out string[] codes);

        Assert.Equal(vector.Expected(), JpxTesting.Raw(image));
        Assert.Equal(["JpxTilePartOrder"], codes);
    }

    [Fact]
    public void An_impossible_tile_part_length_is_recovered_at_the_next_tile_part_with_a_diagnostic()
    {
        JpxVector vector = JpxSamples.Vector("TileParts");

        using DecodedImage image = JpxTesting.DecodeImage(JpxEditing.WithPsot(vector.Data, 1, 5), out string[] codes);

        Assert.Equal(vector.Expected(), JpxTesting.Raw(image));
        Assert.Equal(["JpxPsotInvalid"], codes);
    }

    [Fact]
    public void An_impossible_tile_part_length_throws_in_strict_mode()
    {
        byte[] broken = JpxEditing.WithPsot(JpxSamples.Vector("TileParts").Data, 1, 5);

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => JpxTesting.TryDecodeImage(broken, out _, mode: PdfReadingMode.Strict));

        Assert.Equal("JpxPsotInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void Truncated_irreversible_tiles_keep_what_they_hold_with_a_diagnostic()
    {
        byte[] data = JpxSamples.Vector("IrreversibleIctTiled").Data;

        using DecodedImage image = JpxTesting.DecodeImage(data[..(data.Length * 2 / 3)], out string[] codes);

        Assert.Equal((45, 34, 3), (image.Width, image.Height, image.Components));
        Assert.Contains("JpxCodestreamTruncated", codes);
    }

    [Fact]
    public void A_component_with_no_samples_on_the_image_gives_a_zero_channel()
    {
        // SIZ with YOsiz 1 and YRsiz 106 for the third component: ceil(11 / 106) - ceil(1 / 106) = 0 rows on the image (B-2).
        byte[] data = JpxSamples.Vector("Lrcp3LayersRct").Data;
        data[6 + 14 + 3] = 1;
        data[6 + 36 + 6 + 2] = 106;

        using DecodedImage image = JpxTesting.DecodeImage(data, out _);

        Assert.Equal((19, 10, 3), (image.Width, image.Height, image.Components));
        Assert.All(JpxTesting.Raw(image).Where((_, i) => i % 3 == 2), value => Assert.Equal(0, value));
    }

    [Fact]
    public void With_a_colour_space_of_fewer_components_the_first_components_are_the_channels()
    {
        JpxVector vector = JpxSamples.Vector("Lrcp3LayersRct");

        using DecodedImage image = JpxTesting.DecodeImage(vector.Data, out string[] codes, colorComponents: 1);

        Assert.Equal(1, image.Components);
        Assert.Equal(vector.Expected().Where((_, i) => i % 3 == 0), JpxTesting.Raw(image));
        Assert.Empty(codes);
    }

    [Fact]
    public void With_a_colour_space_of_more_components_the_missing_channels_are_zero_with_a_diagnostic()
    {
        JpxVector vector = JpxSamples.Vector("NoDecomposition");

        using DecodedImage image = JpxTesting.DecodeImage(vector.Data, out string[] codes, colorComponents: 3);

        Assert.Equal(["JpxChannelCountMismatch"], codes);
        int[] raw = JpxTesting.Raw(image);
        Assert.Equal(vector.Expected(), raw.Where((_, i) => i % 3 == 0));
        Assert.All(raw.Where((_, i) => i % 3 != 0), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Bytes_before_the_codestream_are_skipped_with_a_diagnostic()
    {
        byte[] data = [0x00, 0x01, 0x02, .. JpxSamples.WorkedExample];

        using DecodedImage image = JpxTesting.DecodeImage(data, out string[] codes);

        Assert.Equal(["JpxSignatureInvalid"], codes);
        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
    }

    [Fact]
    public void Data_without_a_codestream_decodes_to_nothing_with_a_diagnostic()
    {
        (byte[] decoded, string[] codes) = JpxTesting.Decode([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Empty(decoded);
        Assert.Contains("JpxSignatureInvalid", codes);
    }

    [Fact]
    public void The_header_reports_the_codestream_size_components_precision_and_colour_model()
    {
        var context = new ImageFilterContext(new FilterContext()) { Width = 99, Height = 99 };

        Assert.True(new JpxDecodeFilter().TryReadHeader(JpxSamples.Vector("Tiled").Data, context, out ImageHeader header));
        Assert.Equal(new ImageHeader(21, 17, 3, 8) { ColorModel = ImageColorModel.Rgb }, header);
        Assert.True(new JpxDecodeFilter().TryReadHeader(JpxSamples.Vector("Signed12").Data, context, out header));
        Assert.Equal(new ImageHeader(11, 6, 1, 12) { ColorModel = ImageColorModel.Gray }, header);
    }

    [Fact]
    public void A_bare_codestream_decodes_with_an_information_diagnostic_and_strict_mode_does_not_throw()
    {
        var context = new FilterContext { ReadingMode = PdfReadingMode.Strict };

        using DecodedImage? image = new JpxDecodeFilter().DecodeImage(JpxSamples.WorkedExample, new ImageFilterContext(context));

        Assert.NotNull(image);
        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("JpxCodestreamRaw", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_codestream_in_a_JP2_file_decodes_without_diagnostics()
    {
        using DecodedImage image = JpxTesting.DecodeImage(JpxEditing.Jp2(JpxSamples.WorkedExample), out string[] codes);

        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void A_high_throughput_codestream_signalled_by_a_CAP_marker_is_not_decoded_with_a_diagnostic()
    {
        byte[] file = JpxEditing.Jp2(JpxEditing.WithCapabilities(JpxSamples.WorkedExample, 0x0002_0000, [0x0000]));

        DecodedImage? image = JpxTesting.TryDecodeImage(file, out string[] codes);

        Assert.Null(image);
        Assert.Equal(["JpxHighThroughputUnsupported"], codes);
    }

    [Fact]
    public void A_high_throughput_code_block_style_is_not_decoded_and_throws_in_strict_mode()
    {
        byte[] file = JpxEditing.Jp2(JpxEditing.WithCodeBlockStyle(JpxSamples.WorkedExample, 0x40));

        Assert.Null(JpxTesting.TryDecodeImage(file, out string[] codes));
        Assert.Equal(["JpxHighThroughputUnsupported"], codes);
        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => JpxTesting.TryDecodeImage(file, out _, mode: PdfReadingMode.Strict));
        Assert.Equal("JpxHighThroughputUnsupported", exception.Diagnostic.Code);
    }

    [Fact]
    public void A_capability_other_than_high_throughput_is_skipped_and_the_codestream_decodes()
    {
        byte[] file = JpxEditing.Jp2(JpxEditing.WithCapabilities(JpxSamples.WorkedExample, 0x0000_4000, [0x0000]));

        using DecodedImage image = JpxTesting.DecodeImage(file, out string[] codes);

        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void A_JPX_file_with_more_than_one_codestream_decodes_the_first_with_a_diagnostic()
    {
        byte[] second = JpxSamples.Vector("Tiled").Data;
        byte[] file = [.. JpxEditing.Jp2(JpxSamples.WorkedExample), .. JpxEditing.Box("jp2c", second)];

        using DecodedImage image = JpxTesting.DecodeImage(file, out string[] codes);

        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
        Assert.Equal(["JpxCodestreamsIgnored"], codes);
    }

    [Fact]
    public void A_JPX_file_with_a_fragment_table_reports_that_the_fragmented_codestream_is_not_read()
    {
        byte[] fragmentList = new byte[2 + 14];
        fragmentList[1] = 1;
        byte[] file = [.. JpxEditing.Jp2(JpxSamples.WorkedExample), .. JpxEditing.Box("ftbl", JpxEditing.Box("flst", fragmentList))];

        using DecodedImage image = JpxTesting.DecodeImage(file, out string[] codes);

        Assert.Equal(JpxSamples.WorkedExampleSamples, image.Samples.ToArray());
        Assert.Equal(["JpxCodestreamsIgnored"], codes);
    }
}
