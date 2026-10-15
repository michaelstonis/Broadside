using BenchmarkDotNet.Attributes;
using Broadside.Images.Png;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The PNG reader (W3C PNG Third Edition; issue #153) over a 512 x 512 image built by <see cref="PngSamples"/>, every filter type
/// in turn: RGB (the IDAT data passes through as FlateDecode data, but is still unfiltered to check it), RGBA (colour and alpha
/// planes split) and RGBA Adam7 interlaced. Unfiltering and plane splitting are a hot path: <c>Allocated</c> must not grow with the
/// height (row buffers and planes are pooled); <c>Broadside.Tests.Graphics.PngAllocationTests</c> fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class PngBenchmarks
{
    private byte[] _png = [];

    /// <summary>The layout: <c>rgb</c>, <c>rgba</c> or <c>rgba-interlaced</c>.</summary>
    [Params("rgb", "rgba", "rgba-interlaced")]
    public string Layout { get; set; } = "rgb";

    [GlobalSetup]
    public void Setup()
    {
        _png = PngSamples.Rgb(512, 512, alpha: Layout != "rgb", interlaced: Layout == "rgba-interlaced");
        if (Read() == 0)
        {
            throw new InvalidOperationException($"{Layout} read to nothing.");
        }
    }

    [Benchmark]
    public int Read()
    {
        using PngImage image = PngReader.Read(_png, long.MaxValue, keepSamples: false, out _)!;
        return image.Height;
    }
}
