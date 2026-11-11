using BenchmarkDotNet.Attributes;
using Broadside.Graphics.Colors;
using Broadside.Images;

namespace Broadside.Benchmarks;

/// <summary>
/// The image row conversion kernels of issue #472 on one 2,550-pixel row (the width of the catalogue cover's photograph): packing
/// RGB bytes into BGRA pixels, opaque and premultiplied by an opacity row (ISO 32000-2 §11.6.5.2), as the software rasterizer's image
/// source and every backend's image converter do once per image row, and expanding 8-bit gray to RGB (§10.4.2.2). <c>Scalar</c> is the
/// scalar path (the packing before #472); the others go through the dispatcher (the <c>Vector128</c> path where it is accelerated).
/// <c>GrayToRgbTable</c> is the gray conversion before #472, a three-byte table lookup per sample. <c>Allocated</c> must read
/// <c>-</c>; bit-exactness is <c>Broadside.Tests.Images.PixelPackingKernelTests</c> and
/// <c>Broadside.Tests.Colors.GrayExpansionKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class PixelPackingBenchmarks
{
    private const int Width = 2550;
    private readonly byte[] _rgb = new byte[3 * Width];
    private readonly byte[] _alpha = new byte[Width];
    private readonly byte[] _gray = new byte[Width];
    private readonly byte[] _table = new byte[3 * 256];
    private readonly byte[] _output = new byte[4 * Width];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(472);
        random.NextBytes(_rgb);
        random.NextBytes(_alpha);
        random.NextBytes(_gray);
#pragma warning restore CA5394
        for (int b = 0; b < 256; b++)
        {
            _table[3 * b] = _table[(3 * b) + 1] = _table[(3 * b) + 2] = (byte)b;
        }
    }

    [Benchmark]
    public byte PackOpaqueScalar()
    {
        PixelPacking.PackBgraScalar(_rgb, default, _output);
        return _output[0];
    }

    [Benchmark]
    public byte PackOpaque()
    {
        PixelPacking.PackBgra(_rgb, default, _output);
        return _output[0];
    }

    [Benchmark]
    public byte PackPremultipliedScalar()
    {
        PixelPacking.PackBgraScalar(_rgb, _alpha, _output);
        return _output[0];
    }

    [Benchmark]
    public byte PackPremultiplied()
    {
        PixelPacking.PackBgra(_rgb, _alpha, _output);
        return _output[0];
    }

    [Benchmark]
    public byte GrayToRgbTable()
    {
        ReadOnlySpan<byte> table = _table;
        Span<byte> destination = _output;
        for (int i = 0; i < Width; i++)
        {
            table.Slice(_gray[i] * 3, 3).CopyTo(destination.Slice(i * 3, 3));
        }

        return _output[0];
    }

    [Benchmark]
    public byte GrayToRgbScalar()
    {
        GrayExpansion.ToRgbScalar(_gray, _output.AsSpan(0, 3 * Width));
        return _output[0];
    }

    [Benchmark]
    public byte GrayToRgb()
    {
        GrayExpansion.ToRgb(_gray, _output.AsSpan(0, 3 * Width));
        return _output[0];
    }
}
