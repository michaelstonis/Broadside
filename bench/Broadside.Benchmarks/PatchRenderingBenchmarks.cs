using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer painting Coons and tensor-product patch meshes (ISO 32000-2 §8.7.4.5.7, §8.7.4.5.8; ADR 0006): a grid
/// of 24 cells cycling through <c>sh</c> of a DeviceRGB Coons mesh and of a DeviceCMYK tensor-product mesh (triangles split where
/// the conversion bends), a fill in a Coons pattern with a Function and a Background, and a stroke in a Coons pattern; each mesh is
/// 4 × 4 curved patches, cut into a grid sized to the device scale at every paint. <c>Allocated</c> must read <c>-</c>:
/// <c>Broadside.Rendering.Tests.MeshShadingAllocationTests</c> fails the build if that regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class PatchRenderingBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(MeshShadingSamples.Patches());
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(Dpi);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _bitmap = new Bitmap(size.Width, size.Height);
        _rasterizer = new SoftwareRasterizer();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
    }

    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
