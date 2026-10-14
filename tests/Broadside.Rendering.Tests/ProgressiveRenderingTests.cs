using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Cancellation, progress and time-sliced rendering in the software rasterizer (issue #97, viewer capability 2): a render drawn in
/// slices that resume where they stopped finishes with the same pixels as one uninterrupted render; a cancelled render leaves the
/// rasterizer as it was; a page render reports progress that never decreases and ends at 1.
/// </summary>
public sealed class ProgressiveRenderingTests
{
    public static TheoryData<string> Samples => new(SampleNames);

    private static string[] SampleNames => ["paths", "strokes", "clips", "images", "masks", "text", "shadings", "clipping-paths.pdf"];

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_render_sliced_into_5_ms_budgets_and_resumed_matches_one_uninterrupted_render(string sample)
    {
        using PdfDocument document = PdfDocument.Open(Open(sample));
        (DisplayList list, Matrix device, byte[] expected, int width, int height) = RenderWhole(document, scale: 2);
        using var rasterizer = new SoftwareRasterizer();
        using var bitmap = new Bitmap(width, height);

        using ProgressiveRender render = rasterizer.BeginRender(list, bitmap, device, CancellationToken.None, progress: null);
        int slices = 1;
        while (!render.Continue(TimeSpan.FromMilliseconds(5)))
        {
            slices++;
        }

        Assert.True(render.IsCompleted);
        Assert.Equal(1, render.Progress);
        Assert.True(expected.AsSpan().SequenceEqual(bitmap.Pixels), $"{slices} slices of 5 ms drew different pixels.");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_render_sliced_at_every_operation_matches_one_uninterrupted_render(string sample)
    {
        // A zero budget draws one operation per slice: every point of the list is a place where a render stops and resumes.
        using PdfDocument document = PdfDocument.Open(Open(sample));
        (DisplayList list, Matrix device, byte[] expected, int width, int height) = RenderWhole(document, scale: 1);
        using var rasterizer = new SoftwareRasterizer();
        using var bitmap = new Bitmap(width, height);

        using ProgressiveRender render = rasterizer.BeginRender(list, bitmap, device, CancellationToken.None, progress: null);
        int slices = 1;
        while (!render.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        Assert.Equal(Math.Max(1, list.OperationCount), slices);
        Assert.True(expected.AsSpan().SequenceEqual(bitmap.Pixels));
    }

    [Theory]
    [InlineData("clips")]
    [InlineData("text")]
    [InlineData("images")]
    public void A_tile_sliced_at_every_operation_matches_the_same_tile_rendered_in_one_go(string sample)
    {
        // A tile skips the paints it does not reach (#96); sliced, it skips the same ones and draws the same pixels.
        using PdfDocument document = PdfDocument.Open(Open(sample));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        Matrix device = new RenderOptions().WithScale(2).GetDeviceTransform(page) * new Matrix(1, 0, 0, 1, -300, -500);
        using var rasterizer = new SoftwareRasterizer();
        using var whole = new Bitmap(160, 120);
        rasterizer.Render(list, whole, device);
        using var sliced = new Bitmap(160, 120);

        using ProgressiveRender render = rasterizer.BeginRender(list, sliced, device, CancellationToken.None, progress: null);
        int slices = 1;
        while (!render.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        Assert.True(slices < list.OperationCount, "Nothing was skipped outside the tile.");
        Assert.True(whole.Pixels.SequenceEqual(sliced.Pixels));
    }

    [Fact]
    public void Each_slice_leaves_the_bitmap_holding_the_operations_drawn_so_far()
    {
        // A partial image is valid: after a slice, the bitmap equals an uninterrupted render of the same list cut at that point.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        PdfPage page = document.Pages[0];
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using var rasterizer = new SoftwareRasterizer();
        using var sliced = new Bitmap(size.Width, size.Height);
        using ProgressiveRender render = rasterizer.BeginRender(page, sliced, options);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (!render.Continue(TimeSpan.Zero))
        {
            seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sliced.Pixels)));
        }

        Assert.True(seen.Count > 3, "The partial images did not change between slices.");
        using Bitmap whole = rasterizer.Render(page, options);
        Assert.True(whole.Pixels.SequenceEqual(sliced.Pixels));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(40)]
    public void A_cancelled_render_leaves_nothing_that_changes_the_next_render_of_the_same_list(int slicesBeforeCancel)
    {
        // Cancelled inside clips and saved states: the next render of the list through the same rasterizer is unchanged.
        using PdfDocument document = PdfDocument.Open(Open("clips"));
        (DisplayList list, Matrix device, byte[] expected, int width, int height) = RenderWhole(document, scale: 1);
        using var rasterizer = new SoftwareRasterizer();
        using var partial = new Bitmap(width, height);
        using var cancellation = new CancellationTokenSource();
        ProgressiveRender render = rasterizer.BeginRender(list, partial, device, cancellation.Token, progress: null);
        for (int slice = 0; slice < slicesBeforeCancel; slice++)
        {
            Assert.False(render.Continue(TimeSpan.Zero));
        }

        Assert.Throws<InvalidOperationException>(() => rasterizer.Render(list, partial, device));
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => render.Continue(TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => render.Continue(TimeSpan.Zero));
        render.Dispose();

        using var next = new Bitmap(width, height);
        rasterizer.Render(list, next, device);
        Assert.True(expected.AsSpan().SequenceEqual(next.Pixels));
        using var resumed = new Bitmap(width, height);
        using ProgressiveRender again = rasterizer.BeginRender(list, resumed, device, CancellationToken.None, progress: null);
        Assert.True(again.Continue(Timeout.InfiniteTimeSpan));
        Assert.True(expected.AsSpan().SequenceEqual(resumed.Pixels));
    }

    [Fact]
    public void A_disposed_render_frees_the_rasterizer_and_keeps_its_partial_image()
    {
        using PdfDocument document = PdfDocument.Open(Open("strokes"));
        (DisplayList list, Matrix device, byte[] expected, int width, int height) = RenderWhole(document, scale: 1);
        using var rasterizer = new SoftwareRasterizer();
        using var partial = new Bitmap(width, height);
        ProgressiveRender render = rasterizer.BeginRender(list, partial, device, CancellationToken.None, progress: null);
        Assert.False(render.Continue(TimeSpan.Zero));
        Assert.False(render.Continue(TimeSpan.Zero));

        render.Dispose();

        Assert.Throws<ObjectDisposedException>(() => render.Continue(TimeSpan.Zero));
        using var next = new Bitmap(width, height);
        rasterizer.Render(list, next, device);
        Assert.True(expected.AsSpan().SequenceEqual(next.Pixels));
        Assert.False(expected.AsSpan().SequenceEqual(partial.Pixels));
    }

    [Fact]
    public void A_page_render_reports_progress_that_never_decreases_and_ends_at_completion()
    {
        using PdfDocument document = PdfDocument.Open(Open("long"));
        var progress = new Recorded();
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithProgress(progress));

        Assert.Contains(progress.Values, value => value is > 0 and < 0.5);
        Assert.Contains(progress.Values, value => value is > 0.5 and < 1);
        for (int i = 1; i < progress.Values.Count; i++)
        {
            Assert.True(progress.Values[i] >= progress.Values[i - 1], $"Progress went from {progress.Values[i - 1]} back to {progress.Values[i]}.");
        }

        Assert.Equal(1, progress.Values[^1]);
        Assert.Single(progress.Values, value => value == 1);
    }

    [Fact]
    public void A_sliced_page_render_reports_progress_at_every_slice_and_ends_at_completion()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        PdfPage page = document.Pages[0];
        var progress = new Recorded();
        RenderOptions options = new RenderOptions().WithProgress(progress);
        PixelSize size = options.GetPixelSize(page);
        using var rasterizer = new SoftwareRasterizer();
        using var bitmap = new Bitmap(size.Width, size.Height);

        using ProgressiveRender render = rasterizer.BeginRender(page, bitmap, options);
        Assert.Equal([0.5], progress.Values);
        while (!render.Continue(TimeSpan.Zero))
        {
            Assert.Equal(0.5 + (render.Progress / 2), progress.Values[^1]);
        }

        Assert.Equal(1, progress.Values[^1]);
        Assert.Single(progress.Values, value => value == 1);
    }

    [Fact]
    public void A_page_render_cancelled_from_its_progress_callback_throws_and_returns_its_bitmap()
    {
        using PdfDocument document = PdfDocument.Open(Open("long"));
        using var cancellation = new CancellationTokenSource();
        var progress = new Recorded(value =>
        {
            if (value > 0.5)
            {
                cancellation.Cancel();
            }
        });
        using var rasterizer = new SoftwareRasterizer();
        RenderOptions options = new RenderOptions().WithCancellation(cancellation.Token).WithProgress(progress);

        Assert.Throws<OperationCanceledException>(() => rasterizer.Render(document.Pages[0], options));

        Assert.True(progress.Values[^1] is > 0.5 and < 1);
        using Bitmap next = rasterizer.Render(document.Pages[0]);
        Assert.Throws<OperationCanceledException>(() => rasterizer.Render(document.Pages[0], new RenderOptions().WithCancellation(new CancellationToken(canceled: true))));
    }

    /// <summary>Renders the document's first page's list in one go at <paramref name="scale"/>, over transparent pixels.</summary>
    private static (DisplayList List, Matrix Device, byte[] Pixels, int Width, int Height) RenderWhole(PdfDocument document, double scale)
    {
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var rasterizer = new SoftwareRasterizer();
        using var bitmap = new Bitmap(size.Width, size.Height);
        rasterizer.Render(list, bitmap, device);
        return (list, device, bitmap.Pixels.ToArray(), size.Width, size.Height);
    }

    private static byte[] Open(string sample) => sample switch
    {
        "paths" => ContentSamples.OnePage(ContentSamples.PathHeavy(32 * 1024)),
        "long" => ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)),
        "strokes" => ContentSamples.OnePage(ContentSamples.StrokeHeavy(16 * 1024), ContentSamples.StrokeHeavyResources),
        "clips" => ContentSamples.OnePage(ClipSamples.Grid(ClipSampleKind.Paths, 6, 8)),
        "images" => ContentSamples.ImageHeavy(),
        "masks" => MaskSamples.MaskHeavy(),
        "text" => TextSamples.Page(ContentSamples.TextHeavy(400)),
        "shadings" => ShadingSamples.Grid(3, 4),
        _ => Corpus.Bytes(sample),
    };

    /// <summary>Records every report synchronously.</summary>
    private sealed class Recorded(Action<double>? onReport = null) : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value)
        {
            Values.Add(value);
            onReport?.Invoke(value);
        }
    }
}
