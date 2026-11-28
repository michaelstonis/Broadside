using System.Text;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary><see cref="PdfDocument.CreateImages"/> and <see cref="PdfImagePageExtensions"/> (issue #642): the format by signature, and scan-to-PDF pages.</summary>
public sealed class ImageImportTests
{
    [Theory]
    [InlineData("multipage.tif", 3)]
    [InlineData("gif-256.gif", 1)]
    [InlineData("bmp-24.bmp", 1)]
    public void Create_images_reads_each_format_by_its_signature(string file, int count)
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Equal(count, document.CreateImages(ImportTesting.File(file)).Count);
    }

    [Fact]
    public void Create_images_reads_png_and_jpeg_as_their_own_methods_do()
    {
        byte[] png = PngSamples.Rgb(8, 8, alpha: false, interlaced: false);
        using PdfDocument document = PdfDocument.Create();

        Assert.Same(document.CreateImageFromPng(png).Stream, Assert.Single(document.CreateImages(png)).Stream);
        Assert.Throws<ArgumentException>(() => document.CreateImages("plain text"u8));
    }

    [Fact]
    public void Append_image_pages_sizes_each_page_by_its_resolution()
    {
        using PdfDocument document = PdfDocument.Create();
        int first = document.Pages.Count;

        document.AppendImagePages(ImportTesting.File("bilevel-g4.tif"));
        document.AppendImagePages(ImportTesting.File("gif-2.gif"));

        Assert.Equal(first + 2, document.Pages.Count);
        PdfRectangle scan = document.Pages[first].MediaBox;
        Assert.Equal(203 * 72 / 200.0, scan.Width, 6);
        Assert.Equal(97 * 72 / 100.0, scan.Height, 6);
        Assert.Equal(45, document.Pages[first + 1].MediaBox.Width, 6);
        Assert.Contains(Invariant(203 * 72 / 200.0) + " 0 0 " + Invariant(97 * 72 / 100.0) + " 0 0 cm", Content(document, first), StringComparison.Ordinal);
    }

    [Fact]
    public void Append_image_pages_applies_the_orientation_by_the_matrix()
    {
        using PdfDocument document = PdfDocument.Create();
        int first = document.Pages.Count;

        document.AppendImagePages(ImportTesting.File("bilevel-orientation6.tif"));

        PdfRectangle box = document.Pages[first].MediaBox;
        double width = 97 * 72 / 100.0;
        double height = 203 * 72 / 200.0;
        Assert.Equal(width, box.Width, 6);
        Assert.Equal(height, box.Height, 6);
        Assert.Contains("0 " + Invariant(-height) + " " + Invariant(width) + " 0 0 " + Invariant(height) + " cm", Content(document, first), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 0)]
    [InlineData(4, 0, 0)]
    public void Placement_puts_the_first_pixel_where_the_orientation_says(int orientation, double x, double y)
    {
        // The image's first pixel is the unit square's top-left corner (0, 1).
        Broadside.Graphics.Matrix matrix = PdfImagePageExtensions.Placement(orientation, 1, 1);

        Assert.Equal(x, matrix.C + matrix.E, 6);
        Assert.Equal(y, matrix.D + matrix.F, 6);
    }

    [Fact]
    public void Every_tiff_page_becomes_a_page_and_a_saved_file_reopens()
    {
        using PdfDocument document = PdfDocument.Create();
        int first = document.Pages.Count;

        document.AppendImagePages(ImportTesting.File("multipage.tif"));
        using var output = new MemoryStream();
        document.Save(output);
        using PdfDocument reopened = PdfDocument.Open(output.ToArray());

        Assert.Equal(first + 3, reopened.Pages.Count);
        Assert.Empty(reopened.Diagnostics);
    }

    private static string Invariant(double value) => value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

    private static string Content(PdfDocument document, int page)
    {
        CosObject contents = document.Resolve(document.Pages[page].Dictionary[new CosName("Contents")]);
        IEnumerable<CosObject> streams = contents is CosArray array ? array : [contents];
        return string.Concat(streams.Select(s => Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(s)).Span)));
    }
}
