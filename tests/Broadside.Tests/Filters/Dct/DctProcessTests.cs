using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The coding processes beyond baseline through the filter contract (ISO 32000-2 §7.4.8; ITU-T T.81): progressive Huffman
/// (Annex G: spectral selection and successive approximation), arithmetic coding (Annex D, sequential §F.2.4 and progressive
/// §G.1.3) and 12-bit extended and progressive frames, each within ±1 of libjpeg-turbo (<c>djpeg -dct int -nosmooth</c>) on the
/// vectors next to this file (provenance in README.md there).
/// </summary>
public sealed class DctProcessTests
{
    [Theory]
    [InlineData("progressive", "testorig", "jpegtran -progressive: interleaved DC with Al 1, AC bands 1-5 and 6-63, refinements")]
    [InlineData("progressive-gray-2x2", "gray-2x2", "one component with factors 2x2: non-interleaved DC and AC scans")]
    [InlineData("progressive-restart", "sampling-420", "restart interval of three MCUs: EOBRUN and predictors reset mid-row")]
    [InlineData("progressive-scans", "sampling-444", "three successive-approximation steps for DC and AC, bands out of order")]
    [InlineData("arithmetic", "sampling-420", "sequential arithmetic coding (SOF9) with a DAC segment and restart intervals")]
    [InlineData("testimgari", "testimgint", "libjpeg's arithmetic-coded test image")]
    [InlineData("arithmetic-progressive", "testorig", "progressive arithmetic coding (SOF10)")]
    [InlineData("arithmetic-progressive-scans", "sampling-444", "progressive arithmetic coding with deep refinement and restart intervals")]
    public void Progressive_and_arithmetic_coded_data_decodes_like_the_sequential_original(string vector, string reference, string structure)
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg(vector));

        AssertWithinOne(Golden(reference), samples);
        Assert.True(context.Diagnostics.Count == 0, $"{structure}: {string.Join(", ", Codes(context))}");
    }

    [Theory]
    [InlineData("precision12", "precision12", "extended sequential, 4:2:0, restart interval")]
    [InlineData("precision12-gray", "precision12-gray", "extended sequential, one component, 16-bit quantization tables")]
    [InlineData("precision12-progressive", "precision12", "progressive Huffman")]
    [InlineData("precision12-arithmetic", "precision12", "progressive arithmetic coding")]
    public void Twelve_bit_data_decodes_to_eight_bit_samples_within_one_of_the_reduced_reference(string vector, string reference, string structure)
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg(vector));

        AssertWithinOne(Golden(reference), samples);
        Assert.True(Codes(context) is ["DctPrecisionReduced"], structure);
        Assert.Equal(DiagnosticSeverity.Information, context.Diagnostics[0].Severity);
    }

    [Fact]
    public void Twelve_bit_and_progressive_data_open_in_strict_mode()
    {
        (byte[] twelve, _) = DecodeBytes(Jpeg("precision12-progressive"), mode: PdfReadingMode.Strict);
        (byte[] arithmetic, _) = DecodeBytes(Jpeg("arithmetic-progressive"), mode: PdfReadingMode.Strict);

        AssertWithinOne(Golden("precision12"), twelve);
        AssertWithinOne(Golden("testorig"), arithmetic);
    }

    [Fact]
    public void The_image_facet_reports_eight_bits_for_a_twelve_bit_frame()
    {
        var context = new ImageFilterContext(new FilterContext());
        var filter = new DctDecodeFilter();

        Assert.True(filter.TryReadHeader(Jpeg("precision12"), context, out ImageHeader header));
        using DecodedImage decoded = filter.DecodeImage(Jpeg("precision12"), context)!;

        Assert.Equal(new ImageHeader(75, 43, 3, 8) { ColorModel = ImageColorModel.Rgb }, header);
        Assert.Equal((8, 43), (decoded.BitsPerComponent, decoded.DecodedRows));
        AssertWithinOne(Golden("precision12"), decoded.Samples);
    }

    [Fact]
    public void A_truncated_progressive_image_keeps_every_row_at_lower_precision()
    {
        byte[] jpeg = Jpeg("progressive");
        DctGolden golden = Golden("testorig");
        var context = new ImageFilterContext(new FilterContext());

        // Cut inside the AC scans: the first (DC) scan covered the whole image, so every row is decoded, only less precisely.
        using DecodedImage decoded = new DctDecodeFilter().DecodeImage(jpeg.AsMemory(0, jpeg.Length * 3 / 4), context)!;

        Assert.Equal((149, 149), (decoded.Height, decoded.DecodedRows));
        Assert.Equal(["DctTruncated"], Codes(context.Filter));
        int close = 0;
        for (int i = 0; i < golden.Samples.Length; i++)
        {
            close += Math.Abs(decoded.Samples[i] - golden.Samples[i]) <= 24 ? 1 : 0;
        }

        Assert.True(close > golden.Samples.Length * 9 / 10, $"Only {close} of {golden.Samples.Length} samples are near the reference.");
    }

    [Fact]
    public void A_progressive_scan_with_invalid_parameters_is_skipped_and_reported()
    {
        byte[] jpeg = Jpeg("progressive");
        int sos = DctRepairTests.FindMarker(jpeg, 0xDA);
        int components = jpeg[sos + 4];
        jpeg[sos + 5 + (2 * components) + 1] = 5; // the first scan, a DC scan: Se 5

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        Assert.Equal(Golden("testorig").Samples.Length, samples.Length);
        Assert.Equal(["DctScanInvalid"], Codes(context));
    }

    [Fact]
    public void An_invalid_arithmetic_conditioning_value_is_reported_and_the_defaults_stay()
    {
        byte[] jpeg = Jpeg("arithmetic");
        int dac = DctRepairTests.FindMarker(jpeg, 0xCC);
        jpeg[dac + 5] = 0x01; // DC table 0: L 1 above U 0

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        AssertWithinOne(Golden("sampling-420"), samples);
        Assert.Equal(["DctTableInvalid"], Codes(context));
    }

    [Fact]
    public void Truncated_arithmetic_data_is_reported()
    {
        byte[] jpeg = Jpeg("arithmetic-progressive");

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg[..(jpeg.Length - 600)]);

        Assert.Equal(Golden("testorig").Samples.Length, samples.Length);
        Assert.Contains("DctTruncated", Codes(context));
    }

    [Fact]
    public void A_baseline_frame_with_twelve_bit_samples_is_decoded_as_extended_and_reported()
    {
        byte[] jpeg = Jpeg("precision12");
        jpeg[DctRepairTests.FindMarker(jpeg, 0xC1) + 1] = 0xC0;

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        AssertWithinOne(Golden("precision12"), samples);
        Assert.Equal(["DctSegmentInvalid", "DctPrecisionReduced"], Codes(context));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void A_frame_of_more_than_four_components_decodes_each_component_from_its_own_scan(int components)
    {
        // T.81 §B.2.2 allows up to 255 components in a frame and §B.2.3 at most four in a scan, so such a frame is coded in several
        // scans. Each component here repeats the single non-interleaved scan of gray-2x2.jpg, so each decodes to its golden.
        byte[] jpeg = Repeated(Jpeg("gray-2x2"), components);
        DctGolden gray = Golden("gray-2x2");

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        Assert.Equal(gray.Samples.SelectMany(value => Enumerable.Repeat(value, components)), samples);
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void The_image_facet_delivers_every_component_of_a_five_component_frame()
    {
        byte[] jpeg = Repeated(Jpeg("gray-2x2"), 5);
        DctGolden gray = Golden("gray-2x2");
        var filter = new DctDecodeFilter();
        var context = new ImageFilterContext(new FilterContext());

        Assert.True(filter.TryReadHeader(jpeg, context, out ImageHeader header));
        using DecodedImage decoded = filter.DecodeImage(jpeg, context)!;

        Assert.Equal(new ImageHeader(gray.Width, gray.Height, 5, 8), header with { ColorModel = ImageColorModel.Unknown });
        Assert.Equal((gray.Width, gray.Height, 5), (decoded.Width, decoded.Height, decoded.Components));
        Assert.Equal(gray.Samples.SelectMany(value => Enumerable.Repeat(value, 5)), decoded.Samples.ToArray());
        Assert.False(decoded.ColorTransformApplied);
    }

    /// <summary>
    /// A baseline frame of <paramref name="components"/> components (ids 1 to n, factors 1x1, the source's quantization table), each
    /// coded by its own copy of the one non-interleaved scan of a single-component JPEG (ITU-T T.81 §B.2.2, §B.2.3, §A.2.2).
    /// </summary>
    private static byte[] Repeated(byte[] single, int components)
    {
        var tables = new List<byte>();
        byte[]? frame = null;
        byte[]? scanHeader = null;
        int entropyStart = -1;
        int position = 2;
        while (entropyStart < 0)
        {
            byte marker = single[position + 1];
            int length = (single[position + 2] << 8) | single[position + 3];
            byte[] segment = single[position..(position + 2 + length)];
            switch (marker)
            {
                case 0xC0:
                    frame = segment;
                    break;
                case 0xDA:
                    scanHeader = segment;
                    entropyStart = position + 2 + length;
                    break;
                case 0xDB or 0xC4 or 0xDD:
                    tables.AddRange(segment);
                    break;
            }

            position += 2 + length;
        }

        int entropyEnd = single.AsSpan().LastIndexOf([(byte)0xFF, (byte)0xD9]);
        byte[] entropy = single[entropyStart..entropyEnd];
        int frameLength = 8 + (3 * components);
        var output = new List<byte> { 0xFF, 0xD8 };
        output.AddRange(tables);
        output.AddRange([0xFF, 0xC0, (byte)(frameLength >> 8), (byte)frameLength, frame![4], frame[5], frame[6], frame[7], frame[8], (byte)components]);
        for (int c = 0; c < components; c++)
        {
            output.AddRange([(byte)(c + 1), 0x11, frame[12]]);
        }

        for (int c = 0; c < components; c++)
        {
            output.AddRange([0xFF, 0xDA, 0x00, 0x08, 0x01, (byte)(c + 1), scanHeader![6], scanHeader[7], scanHeader[8], scanHeader[9]]);
            output.AddRange(entropy);
        }

        output.AddRange([0xFF, 0xD9]);
        return [.. output];
    }
}
