using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer compositing with constant alpha and blend modes (ISO 32000-2 §11.3, §11.6.3, §11.6.4.4; ADR 0006): an
/// opaque shaded backdrop, then 32 cells, each of the 16 blend modes at alpha 1 and 0.5, painted through a fill, a stroke, an
/// image, a stencil mask or a shading, so every paint kind composites through every mode. <c>Allocated</c> must read <c>-</c>:
/// <c>Broadside.Rendering.Tests.CompositingAllocationTests</c> fails the build if that regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class CompositingBenchmarks
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
        _document = PdfDocument.Open(BlendSamples.Grid());
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

/// <summary>
/// The compositing rows (ISO 32000-2 §11.3.6; issue #473), one row at a time (<c>Scalar</c>, the code before #473) and through the
/// dispatcher (the <c>Vector128</c> path where it is accelerated): source-over of a 794-pixel row (a page width at 96 DPI, as a
/// transparency group composites onto its parent) of premultiplied pixels, a third of them transparent, at full coverage; and an
/// opaque color over a 256-pixel span at partial coverage. <c>Allocated</c> must read <c>-</c>; the vector paths' bit-exactness is
/// <c>Broadside.Rendering.Tests.CompositingRowsKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class CompositingRowBenchmarks
{
    private const int GroupWidth = 794;
    private const int SpanWidth = 256;
    private const uint Color = 0xFF3366CCu;
    private readonly uint[] _source = new uint[GroupWidth];
    private readonly uint[] _backdrop = new uint[GroupWidth];
    private readonly uint[] _target = new uint[GroupWidth];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(473);
        for (int i = 0; i < GroupWidth; i++)
        {
            int alpha = random.Next(3) == 0 ? 0 : random.Next(256);
            _source[i] = Premultiplied(random, alpha);
            _backdrop[i] = Premultiplied(random, 255);
        }
#pragma warning restore CA5394
    }

    [Benchmark]
    public uint OverRowScalar()
    {
        _backdrop.CopyTo(_target, 0);
        CompositingRows.OverRowScalar(_source, _target, 255);
        return _target[GroupWidth / 2];
    }

    [Benchmark]
    public uint OverRow()
    {
        _backdrop.CopyTo(_target, 0);
        CompositingRows.OverRow(_source, _target, 255);
        return _target[GroupWidth / 2];
    }

    [Benchmark]
    public uint FillRowScalar()
    {
        Span<uint> span = _target.AsSpan(0, SpanWidth);
        _backdrop.AsSpan(0, SpanWidth).CopyTo(span);
        CompositingRows.FillRowScalar(span, Color, 100);
        return span[SpanWidth / 2];
    }

    [Benchmark]
    public uint FillRow()
    {
        Span<uint> span = _target.AsSpan(0, SpanWidth);
        _backdrop.AsSpan(0, SpanWidth).CopyTo(span);
        CompositingRows.FillRow(span, Color, 100);
        return span[SpanWidth / 2];
    }

#pragma warning disable CA5394 // benchmark input, not security
    private static uint Premultiplied(Random random, int alpha) =>
        ((uint)alpha << 24) | ((uint)(random.Next(256) * alpha / 255) << 16) | ((uint)(random.Next(256) * alpha / 255) << 8) | (uint)(random.Next(256) * alpha / 255);
#pragma warning restore CA5394
}
