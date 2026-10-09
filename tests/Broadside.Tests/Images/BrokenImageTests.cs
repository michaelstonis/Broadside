using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Images;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// Malformed image dictionaries and data: the repair lenient mode makes (ADR 0005) and the diagnostic it records, and that strict
/// mode throws instead (ISO 32000-2 §8.9.5 Table 87: "inconsistent entries shall cause an error").
/// </summary>
public class BrokenImageTests
{
    public static TheoryData<string, string, string> UnusableImages => new()
    {
        { "/Width 0 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "a", "ImageDimensionInvalid" },
        { "/Width -3 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "a", "ImageDimensionInvalid" },
        { "/Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "a", "ImageDimensionInvalid" },
        { "/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 0", "a", "ImageBitsPerComponentInvalid" },
        { "/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 32", "a", "ImageBitsPerComponentInvalid" },
        { "/Width 1 /Height 1 /ColorSpace /DeviceGray", "a", "ImageBitsPerComponentInvalid" },
        { "/Width 1 /Height 1 /BitsPerComponent 8", "a", "ImageColorSpaceMissing" },
        { "/Width 1 /Height 1 /ColorSpace /Pattern /BitsPerComponent 8", "a", "ImageColorSpaceInvalid" },
    };

    public static TheoryData<string, string, string> RepairedImages => new()
    {
        { "/Width 2.0 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "ab", "none" },
        { "/Width 2.7 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "ab", "ImageDimensionInvalid" },
        { "/W 2 /H 1 /CS /DeviceGray /BPC 8", "ab", "ImageKeyAbbreviated" },
        { "/Width 2 /Height 1 /ImageMask true /ColorSpace /DeviceGray", "a", "ImageMaskConflict" },
        { "/Width 2 /Height 1 /ImageMask true /BitsPerComponent 8", "a", "ImageMaskConflict" },
        { "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [0]", "ab", "ImageDecodeInvalid" },
        { "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [0 1 0 1]", "ab", "ImageDecodeInvalid" },
        { "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode /Inverted", "ab", "ImageDecodeInvalid" },
        { "/Width 2 /Height 1 /ImageMask true /Decode [0.2 0.7]", "a", "ImageDecodeInvalid" },
        { "/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8", "abc", "ImageDataTruncated" },
    };

    [Theory]
    [MemberData(nameof(UnusableImages))]
    public void An_image_its_dictionary_makes_unpaintable_decodes_to_nothing_with_a_diagnostic(string entries, string data, string code)
    {
        using (PdfDocument document = PdfDocument.Open(OneImage(entries, data)))
        {
            Assert.Null(document.Pages[0].GetImage("Im0")!.Decode());
            Assert.Contains(code, Codes(document));
        }

        using PdfDocument strict = PdfDocument.Open(OneImage(entries, data), new PdfOptions().UseStrict());
        Assert.Equal(code, Assert.Throws<DiagnosticException>(() => strict.Pages[0].GetImage("Im0")!.Decode()).Diagnostic.Code);
    }

    [Theory]
    [MemberData(nameof(RepairedImages))]
    public void A_repairable_image_decodes_with_a_diagnostic_and_strict_mode_throws(string entries, string data, string code)
    {
        using (PdfDocument document = PdfDocument.Open(OneImage(entries, data)))
        {
            using DecodedImage? decoded = document.Pages[0].GetImage("Im0")!.Decode();
            Assert.NotNull(decoded);
            Assert.Equal(code == "none" ? [] : [code], Codes(document));
        }

        if (code != "none")
        {
            using PdfDocument strict = PdfDocument.Open(OneImage(entries, data), new PdfOptions().UseStrict());
            Assert.Equal(code, Assert.Throws<DiagnosticException>(() => strict.Pages[0].GetImage("Im0")!.Decode()).Diagnostic.Code);
        }
    }

    [Fact]
    public void Truncated_data_keeps_the_complete_rows_and_zero_fills_the_rest()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 2 /Height 3 /ColorSpace /DeviceGray /BitsPerComponent 8", "abc"));
        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(1, decoded.DecodedRows);
        Assert.Equal("abc\0\0\0"u8.ToArray(), decoded.Samples.ToArray());
    }

    [Fact]
    public void The_missing_rows_of_a_truncated_image_mask_paint_nothing()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 8 /Height 3 /ImageMask true", Bytes(0x0F)));
        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(1, decoded.DecodedRows);
        Assert.Equal(new byte[] { 0x0F, 0xFF, 0xFF }, decoded.Samples.ToArray());
    }

    [Fact]
    public void Data_longer_than_the_image_is_ignored_with_information()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "abcdef"));
        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal("a"u8.ToArray(), decoded.Samples.ToArray());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("ImageDataTooLong", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_Mask_stream_that_is_not_an_image_mask_is_ignored()
    {
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask 6 0 R",
            "a",
            Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 1", Bytes(0))));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Equal(PdfImageMaskKind.None, image.MaskKind);
        Assert.Null(image.Mask);
        Assert.Equal(["ImageMaskInvalid"], Codes(document));
    }

    [Fact]
    public void A_colour_key_array_too_short_is_ignored_and_one_too_long_is_cut()
    {
        using (PdfDocument shortKey = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask [0 1 0 1]", "abc")))
        {
            PdfImage image = shortKey.Pages[0].GetImage("Im0")!;
            Assert.Equal(PdfImageMaskKind.None, image.MaskKind);
            Assert.Null(image.ColorKey);
            Assert.Equal(["ImageMaskInvalid"], Codes(shortKey));
        }

        using PdfDocument longKey = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask [3 4 5 6]", "a"));
        PdfImage keyed = longKey.Pages[0].GetImage("Im0")!;
        Assert.Equal(PdfImageMaskKind.ColorKey, keyed.MaskKind);
        Assert.Equal([3.0, 4.0], keyed.ColorKey!);
        Assert.Equal(["ImageMaskInvalid"], Codes(longKey));
    }

    [Fact]
    public void SMask_wins_over_Mask_and_a_non_stream_SMask_falls_through_to_Mask()
    {
        string smask = Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "a");
        using (PdfDocument both = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 6 0 R /Mask [0 255]", "a", smask)))
        {
            Assert.Equal(PdfImageMaskKind.Soft, both.Pages[0].GetImage("Im0")!.MaskKind);
            Assert.Empty(both.Diagnostics);
        }

        using PdfDocument named = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask /Luminosity /Mask [0 255]", "a"));
        Assert.Equal(PdfImageMaskKind.ColorKey, named.Pages[0].GetImage("Im0")!.MaskKind);
        Assert.Equal(["ImageSoftMaskInvalid"], Codes(named));
    }

    [Fact]
    public void A_soft_mask_that_is_not_gray_is_read_as_gray_when_it_has_one_component_and_unusable_otherwise()
    {
        using (PdfDocument oneComponent = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 6 0 R", "a",
            Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace [/CalGray << /WhitePoint [0.9505 1 1.089] >>] /BitsPerComponent 8", "a"))))
        {
            PdfImage softMask = oneComponent.Pages[0].GetImage("Im0")!.Mask!;
            Assert.Same(PdfDeviceGrayColorSpace.Instance, softMask.ColorSpace);
            Assert.NotNull(softMask.Decode());
            Assert.Equal(["ImageSoftMaskInvalid"], Codes(oneComponent));
        }

        using PdfDocument rgb = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 6 0 R", "a",
            Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8", "abc")));
        PdfImage rgbMask = rgb.Pages[0].GetImage("Im0")!.Mask!;
        Assert.Null(rgbMask.Decode());
        Assert.Contains("ImageSoftMaskInvalid", Codes(rgb));
    }

    [Fact]
    public void A_mask_without_a_usable_size_takes_its_image_size()
    {
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 8 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask 6 0 R",
            "abcdefgh",
            Stream("/Type /XObject /Subtype /Image /Width 0 /ImageMask true", Bytes(0xF0))));
        PdfImage mask = document.Pages[0].GetImage("Im0")!.Mask!;

        Assert.Equal((8, 1), (mask.Width, mask.Height));
        using DecodedImage decoded = mask.Decode()!;
        Assert.Equal(new byte[] { 0xF0 }, decoded.Samples.ToArray());
        Assert.Equal(["ImageDimensionInvalid"], Codes(document));
    }

    [Fact]
    public void A_Matte_of_the_wrong_length_or_on_a_soft_mask_of_another_size_is_ignored()
    {
        using (PdfDocument wrongLength = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R", "abc",
            Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [1]", "a"))))
        {
            Assert.Null(wrongLength.Pages[0].GetImage("Im0")!.Matte);
            Assert.Equal(["ImageMatteInvalid"], Codes(wrongLength));
        }

        using PdfDocument wrongSize = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 6 0 R", "a",
            Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [0]", "ab")));
        Assert.Null(wrongSize.Pages[0].GetImage("Im0")!.Matte);
        Assert.Equal(["ImageMatteInvalid"], Codes(wrongSize));
    }

    [Fact]
    public void An_Indexed_image_Matte_has_the_components_of_the_base_space()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace [/Indexed /DeviceRGB 0 <FF0000>] /BitsPerComponent 8 /SMask 6 0 R", "\0",
            Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [0 0 1]", "a")));

        Assert.Equal([0.0, 0.0, 1.0], document.Pages[0].GetImage("Im0")!.Matte!);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void SMaskInData_on_data_that_is_not_JPEG_2000_is_ignored()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMaskInData 1", "a"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Equal(PdfImageMaskKind.None, image.MaskKind);
        Assert.Equal(["ImageSoftMaskInDataIgnored"], Codes(document));
    }

    [Fact]
    public void Alternates_are_read_in_order_and_a_malformed_entry_is_skipped()
    {
        using PdfDocument document = PdfDocument.Open(OneImage(
            "/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Alternates [<< /Image 6 0 R /DefaultForPrinting true >> << /Image 7 >> << /Image 6 0 R /OC << /Type /OCG /Name (x) >> >>]",
            "a",
            Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Alternates [<< /Image 6 0 R >>]", "ab")));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        IReadOnlyList<PdfAlternateImage> alternates = image.Alternates;
        Assert.Equal(2, alternates.Count);
        Assert.True(alternates[0].DefaultForPrinting);
        Assert.Equal(2, alternates[0].Image.Width);
        Assert.Empty(alternates[0].Image.Alternates);
        Assert.False(alternates[1].DefaultForPrinting);
        Assert.NotNull(alternates[1].OptionalContent);
        Assert.Equal(["ImageAlternatesInvalid"], Codes(document));
    }

    [Fact]
    public void An_XObject_of_another_subtype_is_not_an_image_and_a_stream_without_Subtype_is()
    {
        using PdfDocument document = PdfDocument.Open(OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "a",
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]", string.Empty),
            Stream("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "a")));

        Assert.Null(document.GetImage(new Broadside.Objects.CosReference(6, 0)));
        Assert.NotNull(document.GetImage(new Broadside.Objects.CosReference(7, 0)));
        Assert.Null(document.GetImage(new Broadside.Objects.CosReference(1, 0)));
        Assert.Null(document.Pages[0].GetImage("Missing"));
    }
}
