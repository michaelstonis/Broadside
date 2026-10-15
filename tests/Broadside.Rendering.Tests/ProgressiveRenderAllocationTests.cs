using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Slicing, cancellation checks and progress cost nothing per operation (issue #97; CLAUDE.md, "Code conventions"): a cancellable
/// render with a progress receiver drawn one operation per slice allocates only its two small objects.
/// <c>ProgressiveRenderBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ProgressiveRenderAllocationTests
{
    [Fact]
    public void A_render_sliced_at_every_operation_allocates_only_the_render()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        using var cancellation = new CancellationTokenSource();
        var progress = new CountingProgress();
        int slices = 0;
        Action render = () =>
        {
            using ProgressiveRender sliced = rasterizer.BeginRender(list, bitmap, device, cancellation.Token, progress);
            while (!sliced.Continue(TimeSpan.Zero))
            {
                slices++;
            }
        };

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.True(slices > 1000);
        Assert.InRange(allocated, 0, 256);
    }

    private sealed class CountingProgress : IProgress<double>
    {
        public int Count { get; private set; }

        public void Report(double value) => Count++;
    }
}
