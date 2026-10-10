using Broadside.Diagnostics;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// Damaged and unusual DCT data through the filter contract: lenient mode decodes what it can and reports each deviation once
/// (ADR 0005), strict mode throws at the first; unsupported processes decode nothing.
/// </summary>
public class DctRepairTests
{
    [Fact]
    public void A_frame_with_zero_lines_takes_its_height_from_the_DNL_segment()
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(Jpeg("dnl"));

        AssertWithinOne(Golden("sampling-420"), samples);
        Assert.Equal(["DctLinesFromDnl"], Codes(context));
    }

    [Fact]
    public void A_frame_with_zero_lines_and_no_DNL_segment_takes_the_image_dictionary_height()
    {
        byte[] jpeg = WithoutDnl(Jpeg("dnl"));
        var context = new Broadside.Filters.FilterContext { StreamDictionary = new Broadside.Objects.CosDictionary { [new("Height")] = new Broadside.Objects.CosInteger(43) } };
        var output = new System.Buffers.ArrayBufferWriter<byte>();

        new Broadside.Filters.DctDecodeFilter().Decode(jpeg, output, context);

        AssertWithinOne(Golden("sampling-420"), output.WrittenSpan);
        Assert.Equal(["DctLinesFromDnl"], Codes(context));
    }

    [Fact]
    public void Scans_without_Huffman_tables_use_the_typical_tables_of_Annex_K()
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(Jpeg("no-dht"));

        AssertWithinOne(Golden("sampling-420"), samples);
        Assert.Equal(["DctTableMissing"], Codes(context));
    }

    [Fact]
    public void Bytes_before_SOI_are_skipped()
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes([(byte)'\n', 0, .. Jpeg("testorig")]);

        AssertWithinOne(Golden("testorig"), samples);
        Assert.Equal(["DctLeadingJunk"], Codes(context));
    }

    [Fact]
    public void Data_without_EOI_decodes_completely_and_is_reported()
    {
        byte[] jpeg = Jpeg("testorig");

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg[..^2]);

        AssertWithinOne(Golden("testorig"), samples);
        Assert.Equal(["DctEndMissing"], Codes(context));
    }

    [Fact]
    public void Truncated_data_keeps_every_row_and_leaves_the_missing_blocks_mid_grey()
    {
        byte[] jpeg = Jpeg("testorig");
        DctGolden golden = Golden("testorig");

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg[..(jpeg.Length / 2)]);

        Assert.Equal(golden.Samples.Length, samples.Length);
        int stride = golden.Width * 3;
        AssertWithinOne(golden with { Samples = golden.Samples[..(16 * stride)] }, samples.AsSpan(0, 16 * stride));
        Assert.All(samples[^stride..], sample => Assert.Equal(128, sample));
        Assert.Equal(["DctTruncated"], Codes(context));
    }

    [Fact]
    public void Strict_mode_throws_at_the_first_deviation()
    {
        byte[] jpeg = Jpeg("testorig");

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => DecodeBytes(jpeg[..(jpeg.Length / 2)], mode: PdfReadingMode.Strict));

        Assert.Equal("DctTruncated", error.Diagnostic.Code);
    }

    [Fact]
    public void A_restart_marker_out_of_sequence_is_resynchronized()
    {
        byte[] jpeg = Jpeg("restart-blocks");
        int rst1 = FindMarker(jpeg, 0xD1);
        jpeg[rst1 + 1] = 0xD5;

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg);

        Assert.Equal(Golden("testorig").Samples.Length, samples.Length);
        Assert.Contains("DctRestartInvalid", Codes(context));
    }

    [Fact]
    public void A_lost_restart_marker_leaves_one_interval_empty_and_the_rest_decodes()
    {
        byte[] jpeg = Jpeg("restart-blocks");
        int rst1 = FindMarker(jpeg, 0xD1);
        jpeg[rst1 + 1] = 0xD2;
        DctGolden golden = Golden("testorig");

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg);

        // The bottom MCU row comes after the damage and resynchronizes to exactly the reference.
        int stride = golden.Width * 3;
        AssertWithinOne(golden with { Samples = golden.Samples[^(5 * stride)..] }, samples.AsSpan(samples.Length - (5 * stride)));
        Assert.Contains("DctRestartInvalid", Codes(context));
    }

    [Fact]
    public void A_missing_quantization_table_decodes_nothing()
    {
        byte[] jpeg = Jpeg("testorig");
        int dqt = FindMarker(jpeg, 0xDB);
        jpeg[dqt + 1] = 0xFE; // the DQT segment becomes a comment

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg);

        Assert.Empty(samples);
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("DctTableMissing", DiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
    }

    [Theory]
    [InlineData(0xC3, "lossless")]
    [InlineData(0xC5, "differential sequential (hierarchical)")]
    [InlineData(0xCB, "lossless, arithmetic-coded")]
    [InlineData(0xCD, "differential sequential, arithmetic-coded")]
    public void The_lossless_and_hierarchical_processes_decode_nothing_and_say_why(int marker, string process)
    {
        byte[] jpeg = Jpeg("testorig");
        jpeg[FindMarker(jpeg, 0xC0) + 1] = (byte)marker;

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg);

        Assert.Empty(samples);
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.True((diagnostic.Code, diagnostic.Severity) == ("DctProcessUnsupported", DiagnosticSeverity.Error), process);
    }

    [Theory]
    [InlineData(0x00, "a zero sampling factor")]
    [InlineData(0x51, "a horizontal sampling factor of 5")]
    public void An_unusable_frame_header_decodes_nothing(int samplingFactors, string defect)
    {
        byte[] jpeg = Jpeg("testorig");
        int sof = FindMarker(jpeg, 0xC0);
        jpeg[sof + 11] = (byte)samplingFactors; // the first component's Hi/Vi

        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(jpeg);

        Assert.Empty(samples);
        Assert.True(Codes(context) is ["DctFrameInvalid"], defect);
    }

    [Fact]
    public void Data_that_is_not_JPEG_decodes_nothing()
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes("not a JPEG"u8.ToArray());

        Assert.Empty(samples);
        Assert.Equal(["DctFrameInvalid"], Codes(context));
    }

    [Fact]
    public void An_image_larger_than_the_decoded_length_limit_is_not_decoded()
    {
        var context = new Broadside.Filters.FilterContext { MaxDecodedLength = 1000 };
        var output = new System.Buffers.ArrayBufferWriter<byte>();

        new Broadside.Filters.DctDecodeFilter().Decode(Jpeg("testorig"), output, context);

        Assert.Equal(0, output.WrittenCount);
        Assert.Equal(["ImageTooLarge"], Codes(context));
    }

    /// <summary>The offset of the first <c>FF</c> <paramref name="code"/> marker.</summary>
    internal static int FindMarker(byte[] jpeg, byte code)
    {
        int found = jpeg.AsSpan().IndexOf([(byte)0xFF, code]);
        Assert.True(found >= 0);
        return found;
    }

    private static byte[] WithoutDnl(byte[] jpeg)
    {
        int dnl = FindMarker(jpeg, 0xDC);
        return [.. jpeg[..dnl], .. jpeg[(dnl + 6)..]];
    }
}
