using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// Inline images (ISO 32000-2 §8.9.7) through <see cref="Broadside.Content.ContentContext.GetInlineImage"/>: abbreviation expansion,
/// decoding, and where the data ends. The end-detection vectors include PDFBox's (<c>PDFStreamParserTest</c>, Apache-2.0).
/// </summary>
public sealed class InlineImageTests
{
    public static TheoryData<string, string, string[]> EndVectors => new()
    {
        // PDFBox PDFStreamParserTest: the data ends at the EI followed by white-space and content (or the end).
        { "BI /W 5 /H 1 /CS /G /BPC 8 /F /RL ID\n12345EI Q", "12345", ["BI", "Q"] },
        { "BI /W 5 /H 1 /CS /G /BPC 8 /F /RL ID 12EI5EI", "12EI5", ["BI"] },
        { "BI /W 8 /H 1 /CS /G /BPC 8 /F /RL ID 12EI5EIQEI", "12EI5EIQ", ["BI"] },

        // Binary data with EI between white-space but binary bytes after it: only an EI followed by content is accepted.
        { "BI /W 9 /H 1 /CS /G /BPC 8 /F /RL ID \u0001 EI \u00FF\u0080 EI Q", "\u0001 EI \u00FF\u0080", ["BI", "Q"] },

        // EI followed by text that is not an operator with its operands, then the real one.
        { "BI /W 9 /H 1 /CS /G /BPC 8 /F /RL ID ab EI xyz EI 1 0 0 1 2 3 cm", "ab EI xyz", ["BI", "cm"] },

        // L (PDF 2.0) leads to EI.
        { "BI /W 2 /H 1 /CS /G /BPC 8 /F /AHx /L 5 ID 4549> EI Q", "4549>", ["BI", "Q"] },

        // Unfiltered data: the length W x n x BPC / 8 x H finds EI even when the data holds "EI Q".
        { "BI /W 4 /H 1 /CS /G /BPC 8 ID EI Q EI Q", "EI Q", ["BI", "Q"] },
        { "BI /W 16 /H 1 /IM true ID EIEI Q", "EI", ["BI", "Q"] },

        // ASCII85: the ~> marker, with white-space inside it, and a missing > before EI.
        { "BI /W 4 /H 1 /CS /G /BPC 8 /F /A85 ID 87cURD]i,\"Ebo80~ > EI Q", "87cURD]i,\"Ebo80~ >", ["BI", "Q"] },
        { "BI /W 4 /H 1 /CS /G /BPC 8 /F /A85 ID 87cUR~EI Q", "87cUR~", ["BI", "Q"] },

        // ASCIIHex: the > marker.
        { "BI /W 2 /H 1 /CS /G /BPC 8 /F /AHx ID 41 42> EI Q", "41 42>", ["BI", "Q"] },

        // DCT: the JPEG marker segments are walked to EOI, past an "EI Q" inside an APPn segment and in the entropy-coded data.
        {
            "BI /W 1 /H 1 /CS /G /BPC 8 /F /DCT ID \u00FF\u00D8\u00FF\u00E0\u0000\u0006EI Q\u00FF\u00DA\u0000\u0002EI Q\u00FF\u0000\u00FF\u00D9 EI Q",
            "\u00FF\u00D8\u00FF\u00E0\u0000\u0006EI Q\u00FF\u00DA\u0000\u0002EI Q\u00FF\u0000\u00FF\u00D9",
            ["BI", "Q"]
        },

        // EI as the last bytes of the stream.
        { "BI /W 2 /H 1 /CS /G /BPC 8 /F /RL ID ab EI", "ab", ["BI"] },
    };

    [Fact]
    public void Inline_image_pdf_decodes_its_abbreviated_gray_image()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(File.ReadAllBytes(Corpus.Path("inline-image.pdf")));
        using (document)
        {
            PdfImage image = Assert.Single(collected.Images);
            Assert.True(image.IsInline);
            Assert.Null(image.Stream);
            Assert.Null(image.Reference);
            Assert.Equal((2, 2, 8), (image.Width, image.Height, image.BitsPerComponent));
            Assert.Same(PdfDeviceGrayColorSpace.Instance, image.ColorSpace);
            Assert.True(image.Dictionary.ContainsKey(new CosName("Width")));
            Assert.False(image.Dictionary.ContainsKey(new CosName("W")));
            Assert.Equal(new CosName("DeviceGray"), image.Dictionary[new CosName("ColorSpace")]);
            using DecodedImage decoded = image.Decode()!;
            Assert.Equal(new byte[] { 0x00, 0xFF, 0xFF, 0x00 }, decoded.Samples.ToArray());
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void Abbreviated_filter_names_Decode_Interpolate_and_L_of_inline_image_filters_pdf_are_expanded()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(File.ReadAllBytes(Corpus.Path("inline-image-filters.pdf")));
        using (document)
        {
            PdfImage image = Assert.Single(collected.Images);
            Assert.Equal(["BI", "Q"], collected.Operators.Skip(2));
            var filters = (CosArray)image.Dictionary[new CosName("Filter")];
            Assert.Equal([new CosName("ASCIIHexDecode"), new CosName("FlateDecode")], filters.Cast<CosName>());
            Assert.True(image.Dictionary.ContainsKey(new CosName("DecodeParms")));
            Assert.True(image.Dictionary.ContainsKey(new CosName("Length")));
            Assert.True(image.Interpolate);
            Assert.Equal([1.0, 0.0, 1.0, 0.0, 1.0, 0.0], image.DecodeArray);
            Assert.Same(PdfDeviceRgbColorSpace.Instance, image.ColorSpace);
            using DecodedImage decoded = image.Decode()!;
            Assert.Equal(Enumerable.Range(0, 48).Select(i => (byte)(5 * i)), decoded.Samples.ToArray());
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void The_data_length_of_an_unfiltered_inline_image_finds_the_EI_after_an_EI_inside_its_data()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(File.ReadAllBytes(Corpus.Path("inline-image-ei-in-data.pdf")));
        using (document)
        {
            using DecodedImage decoded = Assert.Single(collected.Images).Decode()!;
            Assert.Equal("\nEI Q \n "u8.ToArray(), decoded.Samples.ToArray());
            Assert.Equal(["q", "cm", "BI", "Q"], collected.Operators);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Theory]
    [MemberData(nameof(EndVectors))]
    public void The_end_of_inline_image_data_is_found_without_trusting_the_first_EI(string content, string data, string[] operators)
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build(content));
        using (document)
        {
            Assert.Equal(Encoding.Latin1.GetBytes(data), Assert.Single(collected.Data));
            Assert.Equal(operators, collected.Operators);
            Assert.DoesNotContain(document.Diagnostics, d => d.Code == "ContentInlineImageInvalid");
        }
    }

    [Fact]
    public void A_wrong_L_falls_back_to_the_data_length_with_a_diagnostic()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build("q BI /W 4 /H 1 /CS /G /BPC 8 /L 2 ID abcd EI Q"));
        using (document)
        {
            Assert.Equal("abcd"u8.ToArray(), Assert.Single(collected.Data));
            Assert.Equal(["ContentInlineImageInvalid"], Codes(document));
        }
    }

    [Fact]
    public void Carriage_return_and_line_feed_after_ID_is_one_white_space_byte_too_many_when_the_length_says_so()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build("q BI /W 2 /H 1 /CS /G /BPC 8 ID\r\nab EI Q"));
        using (document)
        {
            Assert.Equal("ab"u8.ToArray(), Assert.Single(collected.Data));
            Assert.Equal(["q", "BI", "Q"], collected.Operators);
            Assert.Equal(["ContentInlineImageInvalid"], Codes(document));
        }
    }

    [Fact]
    public void An_inline_image_without_any_acceptable_EI_takes_the_last_candidate_with_a_diagnostic()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build("BI /W 2 /H 1 /CS /G /BPC 8 /F /RL ID ab EI \u00FF\u00FE"));
        using (document)
        {
            Assert.Equal("ab"u8.ToArray(), Assert.Single(collected.Data));
            Assert.Contains("ContentInlineImageInvalid", Codes(document));
        }
    }

    [Fact]
    public void A_named_colour_space_resolves_through_the_resources_and_device_abbreviations_never_do()
    {
        byte[] file = new Broadside.Tests.Document.TestPdf().Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /ColorSpace << /CS0 /DeviceCMYK /G /DeviceRGB >> >> /Contents 4 0 R >>",
            Stream(string.Empty, "BI /W 1 /H 1 /CS /CS0 /BPC 8 ID abcd EI BI /W 1 /H 1 /CS /G /BPC 8 ID a EI BI /W 1 /H 1 /CS [/I /RGB 1 <FF000000FF00>] /BPC 8 ID \u0001 EI"),
        ]);
        (PdfDocument document, InlineImageCollector collected) = InlineImages(file);
        using (document)
        {
            Assert.Equal(3, collected.Images.Count);
            Assert.Same(PdfDeviceCmykColorSpace.Instance, collected.Images[0].ColorSpace);
            Assert.Same(PdfDeviceGrayColorSpace.Instance, collected.Images[1].ColorSpace);
            PdfIndexedColorSpace indexed = Assert.IsType<PdfIndexedColorSpace>(collected.Images[2].ColorSpace);
            Assert.Same(PdfDeviceRgbColorSpace.Instance, indexed.Base);
            Assert.Equal(new CosName("Indexed"), ((CosArray)collected.Images[2].Dictionary[new CosName("ColorSpace")])[0]);
            Assert.Equal(new CosName("DeviceRGB"), ((CosArray)collected.Images[2].Dictionary[new CosName("ColorSpace")])[1]);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void An_abbreviation_wins_over_the_full_key_in_an_inline_image()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build("BI /Width 9 /W 2 /H 1 /CS /G /BPC 8 ID ab EI"));
        using (document)
        {
            Assert.Equal(2, Assert.Single(collected.Images).Width);
        }
    }

    [Fact]
    public void JPXDecode_in_an_inline_image_is_recorded_as_not_allowed()
    {
        (PdfDocument document, InlineImageCollector collected) = InlineImages(ContentPdf.Build("BI /W 1 /H 1 /CS /G /BPC 8 /F /JPXDecode ID a EI"));
        using (document)
        {
            Assert.Single(collected.Images);
            Assert.Contains(document.Diagnostics, d => d.Code == "InlineImageFilterNotAllowed" && d.Severity == DiagnosticSeverity.Warning);
        }
    }
}
