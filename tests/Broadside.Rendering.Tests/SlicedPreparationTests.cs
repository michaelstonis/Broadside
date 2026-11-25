using Broadside.Content;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #365: the long steps of a render split into short ones. An image decoded in slices (<see cref="DisplayImage.ContinueDecode"/>),
/// a display list built in slices (<see cref="PdfPage.BeginDisplayList"/>) and a render whose images are prepared in slices give the
/// samples, the list and the pixels of the unsliced path, byte for byte.
/// </summary>
/// <remarks>
/// In the Heavy collection: the cache test asserts the document keeps the list, and the cache's epoch is process-wide, so a test that
/// edits a document at the same time would make the entry stale.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class SlicedPreparationTests
{
    public static TheoryData<string> ImageFiles => new("dct-baseline.pdf", "dct-cmyk.pdf", "dct-progressive.pdf", "jpx-lossless.pdf", "jpx-subsampled.pdf", "jpx-smask-in-data.pdf", "image-smask.pdf");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(ImageFiles))]
    public void An_image_decoded_in_slices_has_the_samples_of_a_whole_decode(string file)
    {
        foreach (int reduction in new[] { 0, 1, 3 })
        {
            using PdfDocument sliced = PdfDocument.Open(Corpus.Bytes(file));
            using PdfDocument whole = PdfDocument.Open(Corpus.Bytes(file));
            DisplayImage[] slicedImages = Images(sliced);
            DisplayImage[] wholeImages = Images(whole);
            Assert.NotEmpty(slicedImages);
            for (int i = 0; i < slicedImages.Length; i++)
            {
                DisplayImage image = slicedImages[i];
                int slices = 1;
                while (!image.ContinueDecode(reduction, TimeSpan.Zero, Token))
                {
                    slices++;
                }

                DisplayImageSamples? expected = wholeImages[i].GetSamplesAt(reduction, Token);
                DisplayImageSamples? actual = image.GetSamplesAt(reduction, Token);
                Assert.Equal(expected is null, actual is null);
                if (expected is { } e && actual is { } a)
                {
                    Assert.Equal(e.Reduction, a.Reduction);
                    Assert.Equal(e.Width, a.Width);
                    Assert.Equal(e.Height, a.Height);
                    for (int y = 0; y < e.Height; y++)
                    {
                        Assert.True(e.GetRow(y).SequenceEqual(a.GetRow(y)), $"{file} at reduction {reduction}: row {y} differs after {slices} slices.");
                    }
                }

                if (file.StartsWith("dct", StringComparison.Ordinal) && reduction == 0 && image.MaxReduction > 0)
                {
                    // One MCU row per slice: an image of more than one row pauses.
                    Assert.True(slices > 1, $"{file} decoded in one slice.");
                }
            }
        }
    }

    [Fact]
    public void A_decode_paused_between_slices_is_finished_by_GetSamplesAt()
    {
        using PdfDocument sliced = PdfDocument.Open(Corpus.Bytes("dct-progressive.pdf"));
        using PdfDocument whole = PdfDocument.Open(Corpus.Bytes("dct-progressive.pdf"));
        DisplayImage image = Images(sliced)[0];
        Assert.False(image.ContinueDecode(0, TimeSpan.Zero, Token));
        DisplayImageSamples actual = image.GetSamplesAt(0, Token)!.Value;
        DisplayImageSamples expected = Images(whole)[0].GetSamplesAt(0, Token)!.Value;
        for (int y = 0; y < expected.Height; y++)
        {
            Assert.True(expected.GetRow(y).SequenceEqual(actual.GetRow(y)));
        }

        Assert.True(image.ContinueDecode(0, TimeSpan.Zero, Token));
    }

    [Fact]
    public void A_cancelled_slice_discards_the_decode_and_the_next_call_starts_again()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("dct-baseline.pdf"));
        DisplayImage image = Images(document)[0];
        Assert.False(image.ContinueDecode(0, TimeSpan.Zero, Token));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.ContinueDecode(0, TimeSpan.Zero, cancelled.Token));
        while (!image.ContinueDecode(0, TimeSpan.Zero, Token))
        {
        }

        Assert.NotNull(image.GetSamplesAt(0, Token));
    }

    [Theory]
    [InlineData("dct-baseline.pdf")]
    [InlineData("dct-progressive.pdf")]
    [InlineData("jpx-subsampled.pdf")]
    [InlineData("image-smask.pdf")]
    [InlineData("image-smask-matte.pdf")]
    [InlineData("image-4bpc-indexed.pdf")]
    [InlineData("transparency-blend-modes.pdf")]
    public void A_render_that_prepares_its_images_in_slices_draws_the_pixels_of_a_whole_render(string file)
    {
        foreach (bool share in new[] { false, true })
        {
            byte[] expected = Render(file, share, sliced: false, out _);
            byte[] actual = Render(file, share, sliced: true, out int slices);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"{file} (shared {share}) drew different pixels in {slices} slices.");
        }
    }

    [Theory]
    [InlineData("text-tagged-spanning-image.pdf", DisplayLayers.Content, DisplayIntent.View)]
    [InlineData("transparency-blend-modes.pdf", DisplayLayers.Content, DisplayIntent.View)]
    [InlineData("shading-radial-cases.pdf", DisplayLayers.Content, DisplayIntent.View)]
    [InlineData("layout-tables.pdf", DisplayLayers.Content, DisplayIntent.View)]
    [InlineData("annotations-link.pdf", DisplayLayers.All, DisplayIntent.View)]
    [InlineData("acroform-fields.pdf", DisplayLayers.All, DisplayIntent.View)]
    [InlineData("acroform-fields.pdf", DisplayLayers.FormFields, DisplayIntent.View)]
    [InlineData("layout-tables.pdf", DisplayLayers.Content, DisplayIntent.Print)]
    public void A_list_built_in_slices_draws_the_pixels_of_a_list_built_whole(string file, DisplayLayers layers, DisplayIntent intent)
    {
        using PdfDocument sliced = PdfDocument.Open(Corpus.Bytes(file));
        using PdfDocument whole = PdfDocument.Open(Corpus.Bytes(file));
        PdfPage page = sliced.Pages[0];
        using DisplayListBuild build = page.BeginDisplayList(layers, intent, new ContentOptions());
        int slices = 1;
        while (!build.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        DisplayList list = build.Result;
        DisplayList expected = whole.Pages[0].CreateDisplayList(layers, intent, new ContentOptions());
        Assert.Equal(expected.OperationCount, list.OperationCount);
        Assert.True(Draw(expected, whole.Pages[0]).AsSpan().SequenceEqual(Draw(list, page)), $"{file}: the list built in {slices} slices draws differently.");
    }

    [Fact]
    public void A_list_built_in_slices_goes_to_the_documents_cache()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("layout-tables.pdf"));
        PdfPage page = document.Pages[0];
        using DisplayListBuild build = page.BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions());
        while (!build.Continue(TimeSpan.Zero))
        {
        }

        Assert.Same(build.Result, page.CreateDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions()));
        using DisplayListBuild again = page.BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions());
        Assert.True(again.IsCompleted);
        Assert.Same(build.Result, again.Result);
    }

    [Fact]
    public void A_list_build_pauses_between_top_level_operators()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("layout-tables.pdf"));
        using DisplayListBuild build = document.Pages[0].BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions());
        Assert.False(build.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => build.Result);
        int slices = 1;
        while (!build.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        Assert.True(slices > 2, $"{slices} slices");
    }

    [Fact]
    public void An_abandoned_list_build_can_be_built_again()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("layout-tables.pdf"));
        DisplayListBuild build = document.Pages[0].BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions());
        Assert.False(build.Continue(TimeSpan.Zero));
        build.Dispose();
        Assert.Throws<ObjectDisposedException>(() => build.Continue(TimeSpan.Zero));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions());
        Assert.True(list.OperationCount > 0);
    }

    private static byte[] Render(string file, bool share, bool sliced, out int slices)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(1.5);
        PixelRectangle area = options.GetPixelRectangle(page);
        Matrix device = options.GetDeviceTransform(page);
        using var rasterizer = new SoftwareRasterizer { ShareConvertedImages = share, ScaledImageDecode = true };
        using var bitmap = new Bitmap(area.Width, area.Height);
        slices = 1;
        if (!sliced)
        {
            rasterizer.Render(list, bitmap, device);
            return bitmap.Pixels.ToArray();
        }

        using ProgressiveRender render = rasterizer.BeginRender(list, bitmap, device, progress: null, Token);
        while (!render.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        return bitmap.Pixels.ToArray();
    }

    private static byte[] Draw(DisplayList list, PdfPage page)
    {
        RenderOptions options = new RenderOptions().WithScale(1);
        PixelRectangle area = options.GetPixelRectangle(page);
        using var rasterizer = new SoftwareRasterizer();
        using var bitmap = new Bitmap(area.Width, area.Height);
        rasterizer.Render(list, bitmap, options.GetDeviceTransform(page));
        return bitmap.Pixels.ToArray();
    }

    private static DisplayImage[] Images(PdfDocument document)
    {
        var collector = new ImageCollector();
        document.Pages[0].CreateDisplayList().Replay(collector);
        return [.. collector.Images];
    }

    /// <summary>Collects the images a replay draws, in order.</summary>
    private sealed class ImageCollector : IRenderingBackend
    {
        public List<DisplayImage> Images { get; } = [];

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if (!Images.Contains(image))
            {
                Images.Add(image);
            }
        }

        public void DrawGroup(in DisplayGroup group) => group.Replay(this);

        public void SaveState()
        {
        }

        public void RestoreState()
        {
        }

        public void SetTransform(in Matrix transform)
        {
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
        }

        public void IntersectClip(PathView path, FillRule rule)
        {
        }

        public void DrawGlyphs(in GlyphRun run)
        {
        }

        public void DrawShading(in DisplayShading shading)
        {
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
        }

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
        }

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
        }

        public void SetSoftMask(in DisplaySoftMask mask)
        {
        }

        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
        }

        public void EndAnnotation()
        {
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }
    }
}
