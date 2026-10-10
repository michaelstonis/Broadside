using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Images;
using Broadside.TestSupport;
using static Broadside.Tests.Filters.Dct.DctVectors;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The <c>DCTDecode</c> filter's image facet and its place in documents: header, metadata, truncation, the ISO 32000-2 Table 13
/// colour-transform rules, chains and inline images (§7.4.8, §8.9).
/// </summary>
public class DctImageFilterTests
{
    [Fact]
    public void An_image_XObject_decodes_through_the_registered_default_filter()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 227 /Height 149 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", Bytes(Jpeg("testorig"))));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((227, 149, 3, 8), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent));
        AssertWithinOne(Golden("testorig"), decoded.Samples);
        Assert.Equal((ImageColorModel.Rgb, true, (int?)null, 149), (decoded.ColorModel, decoded.ColorTransformApplied, decoded.AdobeTransform, decoded.DecodedRows));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_plain_stream_path_delivers_the_same_samples()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 75 /Height 43 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /DCTDecode", Bytes(Jpeg("gray-2x2"))));
        var stream = (CosStream)document.Resolve(new CosReference(5, 0));

        AssertWithinOne(Golden("gray-2x2"), document.DecodeStream(stream).Span);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void DCTDecode_after_another_filter_in_the_chain_decodes()
    {
        byte[] flated = FilterEncoders.Zlib(Jpeg("sampling-422"));
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 75 /Height 43 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter [/FlateDecode /DCTDecode]", Bytes(flated)));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        AssertWithinOne(Golden("sampling-422"), decoded.Samples);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_inline_image_with_the_DCT_abbreviation_decodes()
    {
        string content = $"q 75 0 0 43 0 0 cm BI /W 75 /H 43 /CS /G /BPC 8 /F /DCT ID {Bytes(Jpeg("gray-2x2"))} EI Q";
        byte[] file = new Document.TestPdf().Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>",
            Stream(string.Empty, content),
        ]);

        (PdfDocument document, InlineImageCollector collected) = InlineImages(file);
        using (document)
        {
            using DecodedImage decoded = Assert.Single(collected.Images).Decode()!;

            AssertWithinOne(Golden("gray-2x2"), decoded.Samples);
            Assert.Equal(ImageColorModel.Gray, decoded.ColorModel);
        }
    }

    [Fact]
    public void The_header_gives_the_frame_size_and_components_at_eight_bits_without_reporting()
    {
        var context = new ImageFilterContext(new FilterContext());

        // The DNL vector's frame says 0 lines: the header still gives the 43 of its DNL segment, and reports nothing yet.
        Assert.True(new DctDecodeFilter().TryReadHeader(Jpeg("dnl"), context, out ImageHeader header));

        Assert.Equal(new ImageHeader(75, 43, 3, 8) { ColorModel = ImageColorModel.Rgb }, header);
        Assert.Empty(context.Filter.Diagnostics);
    }

    [Fact]
    public void Truncated_data_reports_the_rows_decoded_from_data()
    {
        byte[] jpeg = Jpeg("testorig");
        var context = new ImageFilterContext(new FilterContext());

        using DecodedImage decoded = new DctDecodeFilter().DecodeImage(jpeg.AsMemory(0, jpeg.Length / 2), context)!;

        Assert.Equal(149, decoded.Height);
        Assert.InRange(decoded.DecodedRows, 16, 148);
        Assert.Equal(0, decoded.DecodedRows % 16);
        DctGolden golden = Golden("testorig");
        int stride = golden.Width * 3;
        AssertWithinOne(golden with { Samples = golden.Samples[..(decoded.DecodedRows * stride)] }, decoded.Samples[..(decoded.DecodedRows * stride)]);
        Assert.Equal(["DctTruncated"], Codes(context.Filter));
    }

    [Fact]
    public void The_APP14_transform_flag_wins_over_the_ColorTransform_parameter()
    {
        var context = new ImageFilterContext(new FilterContext { Parameters = new CosDictionary { [new("ColorTransform")] = new CosInteger(1) } });

        using DecodedImage decoded = new DctDecodeFilter().DecodeImage(Jpeg("rgb"), context)!;

        AssertWithinOne(Golden("rgb"), decoded.Samples);
        Assert.Equal((0, false), (decoded.AdobeTransform, decoded.ColorTransformApplied));
        Assert.Empty(context.Filter.Diagnostics);
    }

    [Fact]
    public void Without_APP14_ColorTransform_0_leaves_three_components_untransformed()
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg("testorig"), "<< /ColorTransform 0 >>");

        // The untransformed samples are YCbCr: converting them with the CCIR 601-1 formulas of Adobe TN 5116 §13 gives the reference.
        byte[] rgb = new byte[samples.Length];
        for (int i = 0; i < samples.Length; i += 3)
        {
            double y = samples[i];
            double cb = samples[i + 1] - 128.0;
            double cr = samples[i + 2] - 128.0;
            rgb[i] = Clamp(y + (1.402 * cr));
            rgb[i + 1] = Clamp(y - (0.3441363 * cb) - (0.71413636 * cr));
            rgb[i + 2] = Clamp(y + (1.772 * cb));
        }

        AssertWithinOne(Golden("testorig"), rgb);
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void Without_APP14_or_a_parameter_three_components_named_R_G_B_are_not_transformed()
    {
        // A documented compatibility fallback from Table 13's default ColorTransform 1 (libjpeg reads R, G, B identifiers as RGB;
        // pdf.js issue11931.pdf needs it), reported as Information so strict mode accepts it.
        byte[] jpeg = WithoutApp14(Jpeg("rgb"));

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        AssertWithinOne(Golden("rgb"), samples);
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("DctColorTransformInferred", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void Without_APP14_ColorTransform_1_transforms_even_components_named_R_G_B()
    {
        byte[] jpeg = WithoutApp14(Jpeg("rgb"));

        (byte[] transformed, _) = DecodeBytes(jpeg, "<< /ColorTransform 1 >>");
        (byte[] untransformed, _) = DecodeBytes(jpeg);

        Assert.NotEqual(untransformed, transformed);
    }

    [Fact]
    public void A_ColorTransform_outside_0_and_1_is_reported_and_ignored()
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg("testorig"), "<< /ColorTransform 7 >>");

        AssertWithinOne(Golden("testorig"), samples);
        Assert.Equal(["DecodeParmsInvalid"], Codes(context));
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static byte[] WithoutApp14(byte[] jpeg)
    {
        int app14 = DctRepairTests.FindMarker(jpeg, 0xEE);
        int length = (jpeg[app14 + 2] << 8) | jpeg[app14 + 3];
        return [.. jpeg[..app14], .. jpeg[(app14 + 2 + length)..]];
    }
}
