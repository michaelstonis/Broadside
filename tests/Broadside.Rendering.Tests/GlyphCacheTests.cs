using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The software rasterizer's glyph cache (issue #370; ISO 32000-2 §9.2.4, §10.7.4): filled glyphs are painted from coverage computed
/// once per font program, glyph, transform and offset inside the pixel. With exact positioning the pixels never depend on the cache
/// (its budget, or what it held before); quantized positioning reuses far more glyphs and moves them by at most an eighth of a pixel.
/// </summary>
public sealed class GlyphCacheTests
{
    [Theory]
    [InlineData("text-standard14.pdf", 1)]
    [InlineData("text-standard14.pdf", 2.37)]
    [InlineData("text-truetype-embedded.pdf", 1.7)]
    [InlineData("text-type1-embedded.pdf", 3.1)]
    [InlineData("text-cff-embedded.pdf", 1)]
    [InlineData("text-rotated.pdf", 1.3)]
    [InlineData("text-render-modes.pdf", 2)]
    public void Exact_positioning_gives_the_same_pixels_without_a_cache_cold_and_warm(string fileName, double scale)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        using var uncached = new SoftwareRasterizer { GlyphCacheBudget = 0 };
        using var cached = new SoftwareRasterizer();

        using Bitmap expected = uncached.Render(page, options);
        using Bitmap cold = cached.Render(page, options);
        using Bitmap warm = cached.Render(page, options);

        Assert.True(expected.Pixels.SequenceEqual(cold.Pixels));
        Assert.True(expected.Pixels.SequenceEqual(warm.Pixels));
        Assert.Equal(0, uncached.GlyphCacheStatistics.Count);
        Assert.True(cached.GlyphCacheStatistics.Hits > 0);
    }

    [Fact]
    public void A_page_drawn_again_paints_every_glyph_from_the_cache()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(1.5);
        Matrix device = options.GetDeviceTransform(page);
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();

        rasterizer.Render(list, bitmap, device);
        PdfCacheStatistics first = rasterizer.GlyphCacheStatistics;
        rasterizer.Render(list, bitmap, device);
        PdfCacheStatistics second = rasterizer.GlyphCacheStatistics;

        Assert.True(first.Misses > 0);
        Assert.Equal(first.Misses, second.Misses);
        Assert.Equal(first.Hits + first.Misses, second.Hits - first.Hits);
        Assert.Equal(first.Count, second.Count);
        Assert.InRange(second.Bytes, 1, second.Budget);
        Assert.Equal(SoftwareRasterizer.DefaultGlyphCacheBudget, second.Budget);
    }

    [Fact]
    public void Tiles_of_a_text_page_have_the_pixels_of_the_whole_page_without_a_cache()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(1.37);
        Matrix device = options.GetDeviceTransform(page);
        using var uncached = new SoftwareRasterizer { GlyphCacheBudget = 0 };
        using var cached = new SoftwareRasterizer();
        using var expected = new Bitmap(128, 128);
        using var actual = new Bitmap(128, 128);

        for (int y = 0; y < 512; y += 128)
        {
            for (int x = 0; x < 512; x += 128)
            {
                Matrix tile = device * Matrix.CreateTranslation(-x, -y);
                expected.Pixels.Fill(0xFF);
                actual.Pixels.Fill(0xFF);
                uncached.Render(list, expected, tile);
                cached.Render(list, actual, tile);
                Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), $"tile at ({x}, {y}) differs");
            }
        }
    }

    [Fact]
    public void A_budget_smaller_than_the_page_evicts_and_keeps_the_pixels()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(2);
        using var reference = new SoftwareRasterizer();
        using var small = new SoftwareRasterizer { GlyphCacheBudget = 4096 };

        using Bitmap expected = reference.Render(page, options);
        using Bitmap first = small.Render(page, options);
        using Bitmap second = small.Render(page, options);

        PdfCacheStatistics statistics = small.GlyphCacheStatistics;
        Assert.True(statistics.Evictions > 0);
        Assert.InRange(statistics.PeakBytes, 1, 4096);
        Assert.True(expected.Pixels.SequenceEqual(first.Pixels));
        Assert.True(expected.Pixels.SequenceEqual(second.Pixels));
    }

    [Fact]
    public void Quantized_positioning_reuses_more_glyphs_and_moves_edges_only_slightly()
    {
        using PdfDocument document = GoldenCorpus.Open("text-standard14.pdf");
        PdfPage page = document.Pages[0];
        using var exact = new SoftwareRasterizer();
        using var quantized = new SoftwareRasterizer();

        using Bitmap expected = exact.Render(page, new RenderOptions().WithScale(1.37));
        using Bitmap actual = quantized.Render(page, new RenderOptions().WithScale(1.37).WithGlyphPositioning(GlyphPositioning.Quantized));

        Assert.True(quantized.GlyphCacheStatistics.Hits > exact.GlyphCacheStatistics.Hits);
        Assert.True(quantized.GlyphCacheStatistics.Count < exact.GlyphCacheStatistics.Count);
        Assert.False(expected.Pixels.SequenceEqual(actual.Pixels));

        // A glyph moved by at most an eighth of a pixel each way sweeps at most a quarter of any pixel's area: 64 levels, plus one
        // of rounding. Over tracemonkey and the text corpus the largest change measured is 63 (docs/benchmarks/glyph-cache.md).
        int largest = 0;
        for (int index = 0; index < expected.Pixels.Length; index++)
        {
            largest = Math.Max(largest, Math.Abs(expected.Pixels[index] - actual.Pixels[index]));
        }

        Assert.InRange(largest, 1, 65);
    }

    [Fact]
    public void List_renders_take_the_rasterizers_positioning_and_page_renders_the_options()
    {
        using PdfDocument document = GoldenCorpus.Open("text-standard14.pdf");
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(1.37);
        DisplayList list = page.CreateDisplayList();
        using var rasterizer = new SoftwareRasterizer { GlyphPositioning = GlyphPositioning.Quantized };
        using Bitmap quantizedPage = rasterizer.Render(page, new RenderOptions().WithScale(1.37).WithGlyphPositioning(GlyphPositioning.Quantized));
        using var listed = new Bitmap(quantizedPage.Width, quantizedPage.Height);
        listed.Pixels.Fill(0xFF);

        rasterizer.Render(list, listed, options.GetDeviceTransform(page));

        Assert.Equal(GlyphPositioning.Exact, new RenderOptions().GlyphPositioning);
        using var fresh = new SoftwareRasterizer();
        Assert.Equal(GlyphPositioning.Exact, fresh.GlyphPositioning);
        Assert.True(quantizedPage.Pixels.SequenceEqual(listed.Pixels));
    }

    [Fact]
    public void Rendering_another_document_empties_the_cache()
    {
        using PdfDocument first = GoldenCorpus.Open("text-standard14.pdf");
        using PdfDocument second = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(200)));
        using var alone = new SoftwareRasterizer();
        using var both = new SoftwareRasterizer();
        using Bitmap expected = alone.Render(second.Pages[0]);

        using Bitmap a = both.Render(first.Pages[0]);
        using Bitmap b = both.Render(second.Pages[0]);

        Assert.Equal(alone.GlyphCacheStatistics.Count, both.GlyphCacheStatistics.Count);
        Assert.Equal(alone.GlyphCacheStatistics.Bytes, both.GlyphCacheStatistics.Bytes);
    }

    [Fact]
    public void The_budget_cannot_be_negative_and_a_new_one_empties_the_cache()
    {
        using PdfDocument document = GoldenCorpus.Open("text-standard14.pdf");
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
        Assert.True(rasterizer.GlyphCacheStatistics.Count > 0);

        rasterizer.GlyphCacheBudget = 1 << 20;

        Assert.Equal(0, rasterizer.GlyphCacheStatistics.Count);
        Assert.Equal(1 << 20, rasterizer.GlyphCacheBudget);
        Assert.Throws<ArgumentOutOfRangeException>(() => rasterizer.GlyphCacheBudget = -1);
    }
}
