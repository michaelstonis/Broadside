using BenchmarkDotNet.Attributes;
using Broadside.Rendering;

namespace Broadside.Benchmarks;

/// <summary>
/// The pixel compare loop of <see cref="VisualComparison"/> (issue #644) over two Letter pages at 96 DPI (816 × 1056), one with a
/// changed band of rows: <c>CompareRows</c> runs every scanline through the per-pixel difference kernel and writes the overlay,
/// the difference and the cell counts; <c>DifferenceScalar</c> and <c>Difference</c> time the kernel alone over the same rows, the
/// scalar method and the dispatcher (the <c>Vector128</c> path where it is accelerated). One operation is one scanline.
/// <c>Allocated</c> must read <c>-</c>; the vector path's bit-exactness is <c>Broadside.Rendering.Tests.PixelDifferenceKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class VisualComparisonBenchmarks
{
    private const int Width = 816;
    private const int Height = 1056;
    private readonly byte[] _old = new byte[Width * Height * 4];
    private readonly byte[] _new = new byte[Width * Height * 4];
    private readonly int[] _cells = new int[((Width + 7) / 8) * ((Height + 7) / 8)];
    private readonly byte[] _row = new byte[Width];
    private Bitmap? _overlay;
    private Bitmap? _difference;

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(644);
        random.NextBytes(_old);
#pragma warning restore CA5394
        _old.CopyTo(_new, 0);
        _new.AsSpan(400 * Width * 4, 100 * Width * 4).Fill(0x80);
        _overlay = new Bitmap(Width, Height);
        _difference = new Bitmap(Width, Height);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _overlay?.Dispose();
        _difference?.Dispose();
    }

    [Benchmark(OperationsPerInvoke = Height)]
    public long CompareRows()
    {
        _cells.AsSpan().Clear();
        return VisualComparison.CompareRows(_old, _new, Width, Height, 16, _overlay!, _difference!, _cells, _row);
    }

    [Benchmark(OperationsPerInvoke = Height)]
    public int DifferenceScalar()
    {
        int sum = 0;
        for (int y = 0; y < Height; y++)
        {
            int start = y * Width * 4;
            PixelDifference.MaxChannelDifferenceScalar(_old.AsSpan(start, Width * 4), _new.AsSpan(start, Width * 4), _row);
            sum += _row[y % Width];
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Height)]
    public int Difference()
    {
        int sum = 0;
        for (int y = 0; y < Height; y++)
        {
            int start = y * Width * 4;
            PixelDifference.MaxChannelDifference(_old.AsSpan(start, Width * 4), _new.AsSpan(start, Width * 4), _row);
            sum += _row[y % Width];
        }

        return sum;
    }
}
