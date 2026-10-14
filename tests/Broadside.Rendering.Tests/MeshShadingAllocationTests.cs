using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Painting function-based and triangle-mesh shadings is a rasterizer hot path (CLAUDE.md, "Code conventions"): once warm, it
/// allocates nothing per triangle, scanline or pixel, whatever the resolution. The mesh layer, the sampled lattice and the converted
/// vertex colours live in buffers kept from one render to the next. <c>MeshRenderingBenchmarks</c> shows the same in its
/// <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class MeshShadingAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Painting_mesh_shadings_allocates_nothing_per_triangle_scanline_or_pixel(double dpi)
    {
        // sh of a function-based shading and of RGB and CMYK strips, fills in a lattice and a function-based pattern with a
        // Background, strokes in a mesh pattern: 48 paints, the lattice sampled afresh at each function-based paint.
        using PdfDocument document = PdfDocument.Open(MeshShadingSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Equal(48, recorder.Shadings.Count);
        var options = new RenderOptions().WithDpi(dpi);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value is not (0 or 0xFF));
    }
}
