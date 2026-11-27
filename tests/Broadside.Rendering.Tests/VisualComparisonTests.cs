using System.Globalization;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>Visual comparison of two rendered pages (issue #644): changed pixels, their fraction, regions and the overlay.</summary>
public sealed class VisualComparisonTests
{
    private const double Tolerance = 1.0; // points: a region's bounds are whole pixels at 96 DPI, 0.75 point each

    [Fact]
    public void An_identical_page_has_no_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("compare-old.pdf"));

        using VisualComparison comparison = VisualComparison.Compare(document.Pages[0], document.Pages[0]);

        Assert.Equal(0, comparison.ChangedFraction);
        Assert.Empty(comparison.ChangedRegions);
        Assert.Equal((816, 1056), (comparison.Overlay.Width, comparison.Overlay.Height));
        Assert.True(comparison.Difference.Pixels.IndexOfAnyExcept((byte)0) < 0, "a pixel of the difference is not transparent");

        // Paper is light gray in the overlay, and so is ink that did not change, only darker.
        Assert.Equal(new Rgba(255, 255, 255, 255), comparison.Overlay.At(10, 10));
        Rgba ink = comparison.Overlay.At(100, 400); // inside the image
        Assert.True(ink.R == ink.G && ink.G == ink.B && ink.R is >= 192 and < 255, $"unchanged ink is light gray, not {ink}");
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Empty_pages_compare_as_identical()
    {
        using PdfDocument old = PdfDocument.Open(Page("", 200, 200));
        using PdfDocument @new = PdfDocument.Open(Page("", 200, 200));

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0]);

        Assert.Equal(0, comparison.ChangedFraction);
        Assert.Empty(comparison.ChangedRegions);
    }

    [Fact]
    public void A_moved_image_gives_two_regions_one_where_it_was_and_one_where_it_is()
    {
        using PdfDocument old = PdfDocument.Open(Corpus.Bytes("compare-old.pdf"));
        using PdfDocument @new = PdfDocument.Open(Corpus.Bytes("compare-new.pdf"));

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0]);

        // The text changes are regions near the top (y about 680 to 712); the image's are the 100 x 100 squares at y 400.
        PdfRectangle[] images = [.. comparison.ChangedRegions.Where(region => region.Top < 600)];
        Assert.Equal(2, images.Length);
        AssertNear(new PdfRectangle(72, 400, 172, 500), images.Single(region => region.Left < 200));
        AssertNear(new PdfRectangle(300, 400, 400, 500), images.Single(region => region.Left > 200));
        Assert.Contains(comparison.ChangedRegions, region => region.Bottom > 670 && region.Top < 720);
        Assert.InRange(comparison.ChangedFraction, 0.03, 0.06); // two 133 x 133 pixel squares of 816 x 1056, and the words

        // In the overlay, where only the old image was is red; where only the new one is, cyan.
        Rgba wasThere = comparison.Overlay.At(150, 1056 - 450 * 4 / 3);
        Assert.True(wasThere.R == 255 && wasThere.G < 255, $"old ink is red, not {wasThere}");
        Rgba isThere = comparison.Overlay.At(467, 1056 - 450 * 4 / 3);
        Assert.True(isThere.G == 255 && isThere.B == 255 && isThere.R < 255, $"new ink is cyan, not {isThere}");
        Assert.Equal(isThere, comparison.Difference.At(467, 1056 - 450 * 4 / 3));
        Assert.Equal(new Rgba(0, 0, 0, 0), comparison.Difference.At(10, 10));
    }

    [Fact]
    public async Task The_overlay_of_the_corpus_pair_matches_its_reviewed_snapshot()
    {
        using PdfDocument old = await PdfDocument.OpenAsync(Corpus.Bytes("compare-old.pdf"), TestContext.Current.CancellationToken);
        using PdfDocument @new = await PdfDocument.OpenAsync(Corpus.Bytes("compare-new.pdf"), TestContext.Current.CancellationToken);

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0], new VisualComparisonOptions().WithDpi(72));

        using MemoryStream png = PngSnapshots.Encode(comparison.Overlay);
        await Verify(png, "png");
    }

    [Fact]
    public void A_color_change_is_a_region_over_the_shape()
    {
        using PdfDocument old = PdfDocument.Open(Page("1 0 0 rg 100 100 100 50 re f", 300, 300));
        using PdfDocument @new = PdfDocument.Open(Page("0 0 1 rg 100 100 100 50 re f", 300, 300));

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0]);

        AssertNear(new PdfRectangle(100, 100, 200, 150), Assert.Single(comparison.ChangedRegions));
        Assert.Equal(133.0 * 67 / (400 * 400), comparison.ChangedFraction, 2);

        // Red's lightness is the old side's (green and blue), blue's the new side's (red channel).
        Rgba pixel = comparison.Overlay.At(200, 400 - 167);
        Assert.Equal(new Rgba(29, 77, 77, 255), pixel);
    }

    [Theory]
    [InlineData(VisualAlignment.TopLeft)]
    [InlineData(VisualAlignment.Center)]
    public void Pages_of_different_sizes_are_placed_as_asked(VisualAlignment alignment)
    {
        // A square 10 points in from the top-left corner of each page, and one at the center of each (201 points is 268 pixels).
        using PdfDocument old = PdfDocument.Open(Page("0 g 10 171 20 20 re f 90.5 90.5 20 20 re f", 201, 201));
        using PdfDocument @new = PdfDocument.Open(Page("0 g 10 270 20 20 re f 140 140 20 20 re f", 300, 300));
        VisualComparisonOptions options = new VisualComparisonOptions().WithAlignment(alignment);

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0], options);

        Assert.Equal((400, 400), (comparison.Overlay.Width, comparison.Overlay.Height));
        Assert.Equal(2, comparison.ChangedRegions.Count);
        if (alignment == VisualAlignment.TopLeft)
        {
            // The corner squares meet; the centers differ: the old one's region is where the old square sits, measured from the top-left.
            Assert.Contains(comparison.ChangedRegions, region => Near(new PdfRectangle(140, 140, 160, 160), region));
            Assert.Contains(comparison.ChangedRegions, region => Near(new PdfRectangle(90.5, 189.5, 110.5, 209.5), region));
        }
        else
        {
            // The centers meet; the corner squares differ: the old page sits 66 pixels (49.5 points) in from each side of the new one.
            Assert.Contains(comparison.ChangedRegions, region => Near(new PdfRectangle(10, 270, 30, 290), region));
            Assert.Contains(comparison.ChangedRegions, region => Near(new PdfRectangle(59.5, 220.5, 79.5, 240.5), region));
        }
    }

    [Fact]
    public void Regions_are_in_the_user_space_of_a_rotated_new_page()
    {
        using PdfDocument old = PdfDocument.Open(Page("0 g 20 20 30 40 re f", 300, 200, "/Rotate 90"));
        using PdfDocument @new = PdfDocument.Open(Page("0 g 200 120 30 40 re f", 300, 200, "/Rotate 90"));

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0]);

        Assert.Equal((267, 400), (comparison.Overlay.Width, comparison.Overlay.Height)); // shown turned: 200 x 300 points
        Assert.Equal(2, comparison.ChangedRegions.Count);
        Assert.Contains(comparison.ChangedRegions, region => Near(new PdfRectangle(20, 20, 50, 60), region));
        Assert.Contains(comparison.ChangedRegions, region => Near(new PdfRectangle(200, 120, 230, 160), region));
    }

    [Fact]
    public void The_tolerance_absorbs_anti_aliasing_noise()
    {
        // The same edge a fraction of a pixel apart: the edge pixels' coverage differs by a few levels.
        using PdfDocument old = PdfDocument.Open(Page("0 g 100 100 50 50 re f", 300, 300));
        using PdfDocument @new = PdfDocument.Open(Page(string.Create(CultureInfo.InvariantCulture, $"0 g {100 + NoiseShift} 100 50 50 re f"), 300, 300));

        using VisualComparison exact = VisualComparison.Compare(old.Pages[0], @new.Pages[0], new VisualComparisonOptions().WithTolerance(0).WithMinRegionArea(0));
        using VisualComparison tolerant = VisualComparison.Compare(old.Pages[0], @new.Pages[0]);

        Assert.True(exact.ChangedFraction > 0, "the shift changes the edge pixels");
        Assert.NotEmpty(exact.ChangedRegions);
        Assert.Equal(0, tolerant.ChangedFraction);
        Assert.Empty(tolerant.ChangedRegions);
    }

    [Fact]
    public void Regions_smaller_than_the_minimum_area_are_dropped_but_still_counted()
    {
        using PdfDocument old = PdfDocument.Open(Page("", 300, 300));
        using PdfDocument @new = PdfDocument.Open(Page("0 g 30 30 1.5 1.5 re f 150 150 30 30 re f", 300, 300));

        using VisualComparison comparison = VisualComparison.Compare(old.Pages[0], @new.Pages[0]);
        using VisualComparison everything = VisualComparison.Compare(old.Pages[0], @new.Pages[0], new VisualComparisonOptions().WithMinRegionArea(0));

        AssertNear(new PdfRectangle(150, 150, 180, 180), Assert.Single(comparison.ChangedRegions));
        Assert.Equal(2, everything.ChangedRegions.Count);
        Assert.Equal(everything.ChangedFraction, comparison.ChangedFraction);
    }

    [Fact]
    public void The_options_validate_and_chain()
    {
        var options = new VisualComparisonOptions();
        Assert.Equal((96.0, 16, 16, VisualAlignment.TopLeft), (options.Dpi, options.Tolerance, options.MinRegionArea, options.Alignment));
        Assert.Same(options, options.WithDpi(150).WithTolerance(0).WithMinRegionArea(0).WithAlignment(VisualAlignment.Center));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithDpi(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithDpi(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithTolerance(256));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithTolerance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithMinRegionArea(-1));
        using PdfDocument document = PdfDocument.Open(Page("", 100, 100));
        Assert.Throws<ArgumentNullException>(() => VisualComparison.Compare(null!, document.Pages[0]));
        Assert.Throws<ArgumentNullException>(() => VisualComparison.Compare(document.Pages[0], null!));
    }

    /// <summary>The horizontal shift, in points, of <see cref="The_tolerance_absorbs_anti_aliasing_noise"/>.</summary>
    internal const double NoiseShift = 0.04;

    private static byte[] Page(string content, int width, int height, string pageEntries = "") => ContentSamples.File(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << >> /Contents 4 0 R {pageEntries} >>",
        ContentSamples.Stream(content),
    ]);

    private static bool Near(PdfRectangle expected, PdfRectangle actual) =>
        Math.Abs(expected.Left - actual.Left) <= Tolerance && Math.Abs(expected.Bottom - actual.Bottom) <= Tolerance
        && Math.Abs(expected.Right - actual.Right) <= Tolerance && Math.Abs(expected.Top - actual.Top) <= Tolerance;

    private static void AssertNear(PdfRectangle expected, PdfRectangle actual) =>
        Assert.True(Near(expected, actual), $"expected about {expected}, got {actual}");
}
