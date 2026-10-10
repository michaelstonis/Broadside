using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The JPXDecode filter through its public contract (<see cref="IStreamFilter"/> and the image facet <see cref="IImageFilter"/>):
/// ISO 32000-2 §7.4.9 and ITU-T T.800 | ISO/IEC 15444-1.
/// </summary>
public class JpxDecodeFilterTests
{
    public static TheoryData<string> LosslessVectors => new(JpxSamples.Vectors.Where(v => v.Name is not ("Irreversible" or "Rpcl")).Select(v => v.Name));

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
    [InlineData("Irreversible")]
    [InlineData("Rpcl")]
    public void A_feature_not_decoded_yet_is_recorded_as_information_and_no_image_is_made(string name)
    {
        var context = new FilterContext { ReadingMode = PdfReadingMode.Strict };

        DecodedImage? image = new JpxDecodeFilter().DecodeImage(JpxSamples.Vector(name).Data, new ImageFilterContext(context));

        Assert.Null(image);
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("JpxUnsupportedFeature", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
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
    public void The_header_reports_the_codestream_size_components_and_precision()
    {
        var context = new ImageFilterContext(new FilterContext()) { Width = 99, Height = 99 };

        Assert.True(new JpxDecodeFilter().TryReadHeader(JpxSamples.Vector("Tiled").Data, context, out ImageHeader header));
        Assert.Equal(new ImageHeader(21, 17, 3, 8), header);
        Assert.True(new JpxDecodeFilter().TryReadHeader(JpxSamples.Vector("Signed12").Data, context, out header));
        Assert.Equal(new ImageHeader(11, 6, 1, 12), header);
    }
}
