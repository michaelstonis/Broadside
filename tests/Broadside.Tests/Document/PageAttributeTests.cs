using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Tests.Document;

/// <summary>
/// Page boundaries, rotation and user unit with the defaults the specification defines. ISO 32000-2 §7.7.3.3 Table 31, §7.9.5,
/// §14.11.2.
/// </summary>
public sealed class PageAttributeTests
{
    private static readonly PdfRectangle Letter = new(0, 0, 612, 792);

    [Fact]
    public void Crop_bleed_trim_and_art_boxes_default_to_the_media_box_through_the_crop_box()
    {
        PdfPage page = SinglePage("/MediaBox [0 0 612 792]", out PdfDocument document);
        using (document)
        {
            Assert.Equal(Letter, page.CropBox);
            Assert.Equal(Letter, page.BleedBox);
            Assert.Equal(Letter, page.TrimBox);
            Assert.Equal(Letter, page.ArtBox);
            Assert.Equal(0, page.Rotation);
            Assert.Equal(1.0, page.UserUnit);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void Bleed_trim_and_art_boxes_default_to_the_crop_box()
    {
        PdfPage page = SinglePage("/MediaBox [0 0 612 792] /CropBox [10 20 600 780]", out PdfDocument document);
        using (document)
        {
            var crop = new PdfRectangle(10, 20, 600, 780);
            Assert.Equal(crop, page.CropBox);
            Assert.Equal(crop, page.BleedBox);
            Assert.Equal(crop, page.TrimBox);
            Assert.Equal(crop, page.ArtBox);
        }
    }

    [Fact]
    public void Each_box_reads_its_own_entry()
    {
        PdfPage page = SinglePage(
            "/MediaBox [0 0 612 792] /CropBox [5 5 607 787] /BleedBox [6 6 606 786] /TrimBox [7 7 605 785] /ArtBox [8 8 604 784] /UserUnit 2.5",
            out PdfDocument document);
        using (document)
        {
            Assert.Equal(new PdfRectangle(5, 5, 607, 787), page.CropBox);
            Assert.Equal(new PdfRectangle(6, 6, 606, 786), page.BleedBox);
            Assert.Equal(new PdfRectangle(7, 7, 605, 785), page.TrimBox);
            Assert.Equal(new PdfRectangle(8, 8, 604, 784), page.ArtBox);
            Assert.Equal(2.5, page.UserUnit);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void Rectangles_given_by_any_two_opposite_corners_are_normalized()
    {
        PdfPage page = SinglePage("/MediaBox [612 792 0 0] /CropBox [600 20 10 780]", out PdfDocument document);
        using (document)
        {
            Assert.Equal(Letter, page.MediaBox);
            Assert.Equal(new PdfRectangle(10, 20, 600, 780), page.CropBox);
            Assert.Equal(10, page.CropBox.Left);
            Assert.Equal(780, page.CropBox.Top);
            Assert.Equal(590, page.CropBox.Width);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void Boxes_that_extend_beyond_the_media_box_are_reduced_to_their_intersection_with_it()
    {
        PdfPage page = SinglePage("/MediaBox [0 0 612 792] /CropBox [-10 -10 700 400] /TrimBox [500 700 900 900]", out PdfDocument document);
        using (document)
        {
            Assert.Equal(new PdfRectangle(0, 0, 612, 400), page.CropBox);
            Assert.Equal(new PdfRectangle(500, 700, 612, 792), page.TrimBox);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Fact]
    public void A_missing_media_box_reads_as_us_letter_with_a_diagnostic()
    {
        PdfPage page = SinglePage(string.Empty, out PdfDocument document);
        using (document)
        {
            Assert.Equal(Letter, page.MediaBox);
            Diagnostic diagnostic = Assert.Single(document.Diagnostics);
            Assert.Equal("PageMediaBoxMissing", diagnostic.Code);
            Assert.Equal(new CosReference(3, 0), diagnostic.ObjectReference);
        }
    }

    [Fact]
    public void A_malformed_box_reads_as_its_default_with_a_diagnostic()
    {
        PdfPage page = SinglePage("/MediaBox [0 0 612 792] /CropBox [0 0 100]", out PdfDocument document);
        using (document)
        {
            Assert.Equal(Letter, page.CropBox);
            Assert.Equal("PageBoxInvalid", Assert.Single(document.Diagnostics).Code);
        }
    }

    [Fact]
    public void A_box_and_its_numbers_may_be_indirect_objects()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox 4 0 R >>",
            "[0 0 5 0 R 842]",
            "595"));

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), Assert.Single(document.Pages).MediaBox);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("90", 90)]
    [InlineData("180", 180)]
    [InlineData("270", 270)]
    [InlineData("-90", 270)]
    [InlineData("450", 90)]
    [InlineData("-720", 0)]
    public void Rotation_is_a_multiple_of_90_normalized_into_0_to_270(string rotate, int expected)
    {
        PdfPage page = SinglePage($"/MediaBox [0 0 612 792] /Rotate {rotate}", out PdfDocument document);
        using (document)
        {
            Assert.Equal(expected, page.Rotation);
            Assert.Empty(document.Diagnostics);
        }
    }

    [Theory]
    [InlineData("45", 0)]
    [InlineData("/Ninety", 0)]
    [InlineData("90.0", 90)]
    public void A_rotation_that_is_not_an_integer_multiple_of_90_is_reported(string rotate, int expected)
    {
        PdfPage page = SinglePage($"/MediaBox [0 0 612 792] /Rotate {rotate}", out PdfDocument document);
        using (document)
        {
            Assert.Equal(expected, page.Rotation);
            Assert.Equal("PageRotateInvalid", Assert.Single(document.Diagnostics).Code);
        }
    }

    [Fact]
    public void A_user_unit_that_is_not_positive_reads_as_1_with_a_diagnostic()
    {
        PdfPage page = SinglePage("/MediaBox [0 0 612 792] /UserUnit -2", out PdfDocument document);
        using (document)
        {
            Assert.Equal(1.0, page.UserUnit);
            Assert.Equal("PageUserUnitInvalid", Assert.Single(document.Diagnostics).Code);
        }
    }

    [Fact]
    public void A_page_is_a_live_view_a_change_to_its_dictionary_shows_at_once()
    {
        PdfPage page = SinglePage("/MediaBox [0 0 612 792]", out PdfDocument document);
        using (document)
        {
            page.Dictionary[new CosName("MediaBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(595), new CosInteger(842)]);
            page.Dictionary[new CosName("Rotate")] = new CosInteger(90);

            Assert.Equal(new PdfRectangle(0, 0, 595, 842), page.MediaBox);
            Assert.Equal(new PdfRectangle(0, 0, 595, 842), page.ArtBox);
            Assert.Equal(90, page.Rotation);
            Assert.True(page.Dictionary.IsDirty);
        }
    }

    private static PdfPage SinglePage(string pageEntries, out PdfDocument document)
    {
        document = PdfDocument.Open(TestPdf.OnePage(pageEntries));
        return Assert.Single(document.Pages);
    }
}
