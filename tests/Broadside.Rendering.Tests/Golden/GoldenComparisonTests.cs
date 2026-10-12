using Broadside.GoldenImages;

namespace Broadside.Rendering.Tests.Golden;

/// <summary>
/// Self-tests of the perceptual comparison on synthetic images (issue #120): what the default threshold must catch and what it must
/// let through.
/// </summary>
public sealed class GoldenComparisonTests
{
    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) Blue = (20, 60, 200);

    public static TheoryData<double, bool, bool> Phases()
    {
        var data = new TheoryData<double, bool, bool>();
        foreach (double phase in new[] { 0, 0.25, 0.5, 0.75 })
        {
            foreach (bool horizontal in new[] { true, false })
            {
                foreach (bool blue in new[] { false, true })
                {
                    data.Add(phase, horizontal, blue);
                }
            }
        }

        return data;
    }

    [Fact]
    public void An_image_matches_itself_with_nothing_different()
    {
        GoldenImage glyph = SyntheticGlyphs.Draw(0.3, 0.6, 16, Black);

        GoldenComparison comparison = GoldenComparison.Compare(glyph, glyph, GoldenThreshold.Default);

        Assert.True(comparison.Matches);
        Assert.Equal(0, comparison.DifferingPixels);
        Assert.Equal(0, comparison.TolerantPixels);
        Assert.Equal(0, comparison.MaxDifference);
    }

    [Theory]
    [MemberData(nameof(Phases))]
    public void A_glyph_shifted_by_one_pixel_fails_the_default_threshold_at_every_subpixel_phase(double phase, bool horizontal, bool blue)
    {
        var ink = blue ? Blue : Black;
        GoldenImage expected = SyntheticGlyphs.Draw(phase, phase, 16, ink);
        GoldenImage shifted = SyntheticGlyphs.Draw(phase + (horizontal ? 1 : 0), phase + (horizontal ? 0 : 1), 16, ink);

        GoldenComparison comparison = GoldenComparison.Compare(expected, shifted, GoldenThreshold.Default);

        Assert.False(comparison.Matches, comparison.Describe());
        // Every row (or column) of every stem edge differs by more than the tolerance, not a stray pixel or two.
        Assert.True(comparison.DifferingPixels >= 20, comparison.Describe());
    }

    [Theory]
    [MemberData(nameof(Phases))]
    public void Another_renderers_anti_aliasing_passes_the_default_threshold_at_every_subpixel_phase(double phase, bool exactFirst, bool blue)
    {
        var ink = blue ? Blue : Black;
        GoldenImage exact = SyntheticGlyphs.Draw(phase, phase, 16, ink);

        // Another renderer: coverage from a 4 x 4 grid of samples instead of (near) exact area.
        GoldenImage coarse = SyntheticGlyphs.Draw(phase, phase, 4, ink);

        GoldenComparison comparison = exactFirst
            ? GoldenComparison.Compare(exact, coarse, GoldenThreshold.Default)
            : GoldenComparison.Compare(coarse, exact, GoldenThreshold.Default);

        Assert.True(comparison.Matches, comparison.Describe());
        Assert.True(comparison.TolerantPixels > 20, "The two renders should differ at the edges: " + comparison.Describe());
    }

    [Theory]
    [MemberData(nameof(Phases))]
    public void Edges_placed_a_tenth_of_a_pixel_apart_pass_the_default_threshold_at_every_subpixel_phase(double phase, bool horizontal, bool blue)
    {
        var ink = blue ? Blue : Black;
        GoldenImage expected = SyntheticGlyphs.Draw(phase, phase, 16, ink);

        // Another renderer rounding coordinates differently: every edge moved by a tenth of a pixel.
        GoldenImage nudged = SyntheticGlyphs.Draw(phase + (horizontal ? 0.1 : 0), phase + (horizontal ? 0 : 0.1), 16, ink);

        GoldenComparison comparison = GoldenComparison.Compare(expected, nudged, GoldenThreshold.Default);

        Assert.True(comparison.Matches, comparison.Describe());
        Assert.True(comparison.TolerantPixels > 20, "The two renders should differ at the edges: " + comparison.Describe());
    }

    [Fact]
    public void Images_of_different_sizes_never_match()
    {
        GoldenImage small = GoldenImage.FromPixels(new byte[4 * 4 * 4], 4, 4, 16, GoldenPixelFormat.Rgba8);
        GoldenImage wide = GoldenImage.FromPixels(new byte[5 * 4 * 4], 5, 4, 20, GoldenPixelFormat.Rgba8);

        GoldenComparison comparison = GoldenComparison.Compare(small, wide, new GoldenThreshold(1, 1, int.MaxValue));

        Assert.False(comparison.Matches);
        Assert.Contains("5 x 4", comparison.Describe(), StringComparison.Ordinal);
        Assert.Contains("4 x 4", comparison.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_threshold_allows_exactly_its_number_of_differing_pixels()
    {
        GoldenImage expected = SyntheticGlyphs.Draw(0, 0, 4, Black);
        byte[] spotted = Rgba(expected);
        foreach ((int x, int y) in new[] { (1, 1), (30, 2), (2, 35) })
        {
            spotted[((y * expected.Width) + x) * 4] = 0;
        }

        GoldenImage actual = GoldenImage.FromPixels(spotted, expected.Width, expected.Height, expected.Width * 4, GoldenPixelFormat.Rgba8);

        Assert.True(GoldenComparison.Compare(expected, actual, GoldenThreshold.Default with { MaxDifferingPixels = 3 }).Matches);
        GoldenComparison strict = GoldenComparison.Compare(expected, actual, GoldenThreshold.Default with { MaxDifferingPixels = 2 });
        Assert.False(strict.Matches);
        Assert.Equal(3, strict.DifferingPixels);
        Assert.Equal(new GoldenPixel(1, 1), strict.FirstDifference);
    }

    [Fact]
    public void The_difference_of_two_greys_is_their_share_of_the_way_from_white_to_black()
    {
        GoldenImage white = Solid(255, 255, 255);

        Assert.Equal(1, GoldenComparison.Compare(white, Solid(0, 0, 0), GoldenThreshold.Exact).MaxDifference, 6);
        Assert.Equal(0.2, GoldenComparison.Compare(white, Solid(204, 204, 204), GoldenThreshold.Exact).MaxDifference, 6);
    }

    [Fact]
    public void A_colour_change_over_a_flat_area_fails_where_the_same_change_at_an_edge_passes()
    {
        // A tenth of the way from white to black: beyond the default's 0.05 for flat areas, within its 0.3 for edges.
        GoldenImage white = Solid(255, 255, 255, 3, 3);
        GoldenComparison flat = GoldenComparison.Compare(white, Solid(230, 230, 230, 3, 3), GoldenThreshold.Default);
        Assert.False(flat.Matches);
        Assert.Equal(9, flat.DifferingPixels);

        // The same pixel next to black ink is on an edge.
        GoldenImage inked = GoldenImage.FromPixels([255, 255, 255, 255, 0, 0, 0, 255], 2, 1, 8, GoldenPixelFormat.Rgba8);
        GoldenImage greyer = GoldenImage.FromPixels([230, 230, 230, 255, 0, 0, 0, 255], 2, 1, 8, GoldenPixelFormat.Rgba8);
        GoldenComparison edge = GoldenComparison.Compare(inked, greyer, GoldenThreshold.Default);
        Assert.True(edge.Matches, edge.Describe());
        Assert.Equal(1, edge.TolerantPixels);
    }

    [Fact]
    public void Colour_differences_weigh_less_than_brightness_differences()
    {
        // Pure blue and pure yellow, swapped in the chroma channels but close in brightness weighting, vs a grey step.
        GoldenImage grey = Solid(128, 128, 128);
        double brightness = GoldenComparison.Compare(grey, Solid(160, 160, 160), GoldenThreshold.Exact).MaxDifference;
        double hue = GoldenComparison.Compare(grey, Solid(160, 128, 96), GoldenThreshold.Exact).MaxDifference;

        Assert.True(hue < brightness, $"hue {hue} vs brightness {brightness}");
    }

    [Fact]
    public void Pixels_compare_as_they_appear_on_a_white_page_whatever_their_layout()
    {
        // Fully transparent, in each layout a backend hands over.
        foreach (GoldenPixelFormat format in Enum.GetValues<GoldenPixelFormat>())
        {
            GoldenImage transparent = GoldenImage.FromPixels(new byte[3 * 2 * 4], 3, 2, 12, format);
            Assert.True(GoldenComparison.Compare(Solid(255, 255, 255, 3, 2), transparent, GoldenThreshold.Exact).Matches, format.ToString());
        }

        // Half-transparent red: (255, 0, 0, 128) straight, (128, 0, 0, 128) premultiplied, both read as (255, 127, 127) on white.
        GoldenImage pink = Solid(255, 127, 127);
        byte[] straightRgba = [255, 0, 0, 128];
        byte[] straightBgra = [0, 0, 255, 128];
        byte[] premultipliedRgba = [128, 0, 0, 128];
        byte[] premultipliedBgra = [0, 0, 128, 128];
        Assert.True(Same(pink, straightRgba, GoldenPixelFormat.Rgba8));
        Assert.True(Same(pink, straightBgra, GoldenPixelFormat.Bgra8));
        Assert.True(Same(pink, premultipliedRgba, GoldenPixelFormat.Rgba8Premultiplied));
        Assert.True(Same(pink, premultipliedBgra, GoldenPixelFormat.Bgra8Premultiplied));
    }

    [Fact]
    public void Rows_are_read_through_the_stride()
    {
        // Two rows of one pixel, each row padded to 8 bytes with junk that must be ignored.
        byte[] padded = [0, 0, 0, 255, 9, 9, 9, 9, 255, 255, 255, 255, 9, 9, 9, 9];

        GoldenImage image = GoldenImage.FromPixels(padded, 1, 2, 8, GoldenPixelFormat.Rgba8);

        Assert.Equal((0, 0, 0), image[0, 0]);
        Assert.Equal((255, 255, 255), image[0, 1]);
    }

    [Fact]
    public void The_diff_image_shows_differing_pixels_red_tolerated_ones_amber_and_the_rest_faded()
    {
        GoldenImage expected = Solid(255, 255, 255, 3, 3);
        byte[] pixels = Rgba(expected);
        pixels[0] = pixels[1] = pixels[2] = 0;                  // (0, 0): black, differs
        pixels[4] = pixels[5] = pixels[6] = 247;                // (1, 0): a hint of grey, tolerated
        GoldenImage actual = GoldenImage.FromPixels(pixels, 3, 3, 12, GoldenPixelFormat.Rgba8);

        GoldenImage diff = GoldenComparison.Compare(expected, actual, GoldenThreshold.Default).CreateDiffImage();

        Assert.Equal((255, 0, 0), diff[0, 0]);
        Assert.Equal((255, 176, 0), diff[1, 0]);
        Assert.Equal((255, 255, 255), diff[2, 2]);
    }

    [Fact]
    public void A_png_round_trip_keeps_every_pixel()
    {
        GoldenImage glyph = SyntheticGlyphs.Draw(0.4, 0.2, 4, Blue);
        using var png = new MemoryStream();

        glyph.SavePng(png);
        png.Position = 0;
        GoldenImage decoded = GoldenImage.FromPng(png);

        Assert.True(GoldenComparison.Compare(glyph, decoded, GoldenThreshold.Exact).Matches);
    }

    private static bool Same(GoldenImage expected, byte[] pixel, GoldenPixelFormat format) =>
        GoldenComparison.Compare(expected, GoldenImage.FromPixels(pixel, 1, 1, 4, format), new GoldenThreshold(0.005, 0.005, 0)).Matches;

    private static GoldenImage Solid(byte r, byte g, byte b, int width = 1, int height = 1)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }

        return GoldenImage.FromPixels(pixels, width, height, width * 4, GoldenPixelFormat.Rgba8);
    }

    private static byte[] Rgba(GoldenImage image)
    {
        byte[] pixels = new byte[image.Width * image.Height * 4];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                (byte r, byte g, byte b) = image[x, y];
                int offset = ((y * image.Width) + x) * 4;
                pixels[offset] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }
}
