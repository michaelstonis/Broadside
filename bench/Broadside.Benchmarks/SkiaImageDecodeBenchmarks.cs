using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering.Skia;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #374: SkiaSharp's JPEG decoder (libjpeg-turbo through <c>SKCodec</c>, <see cref="SkiaImageDecoder"/>) at 1/1, 1/2, 1/4 and
/// 1/8 on the catalogue-class photograph (<see cref="CatalogueSamples.Jpeg"/>, 2550 x 3150, baseline, 4:4:4 and 4:2:0), the platform
/// decode the viewer's Skia tiles prefetch. Compare with the managed decoder's <see cref="DctScaledBenchmarks"/> on the same input.
/// At 4:2:0 and full size the viewer does not offer the image to Skia (libjpeg-turbo interpolates the chroma there); this measures
/// that decode anyway, as a bound. The band of RGBA rows and the samples are pooled: what <c>Allocated</c> shows is the codec's
/// wrappers and the image object, per call, the same for any height (<c>SkiaImageDecoderTests</c>).
/// </summary>
[MemoryDiagnoser]
public class SkiaImageDecodeBenchmarks
{
    private byte[] _jpeg = [];
    private PlatformImageRequest _request;

    /// <summary>The chroma subsampling of the photograph: 4:4:4 (the catalogue cover's) or 4:2:0.</summary>
    [Params("444", "420")]
    public string Sampling { get; set; } = "444";

    /// <summary>The denominator of the scale: 1, 2, 4 or 8.</summary>
    [Params(1, 2, 4, 8)]
    public int Scale { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        _jpeg = CatalogueSamples.Jpeg(subsampling: Sampling == "444" ? DctChromaSubsampling.None : DctChromaSubsampling.HorizontalAndVertical);
        _request = new PlatformImageRequest
        {
            Filter = new CosName("DCTDecode"),
            Width = 2550,
            Height = 3150,
            Components = 3,
            BitsPerComponent = 8,
            ColorTransform = true,
            ChromaHorizontalSubsampling = 1,
            ChromaVerticalSubsampling = 1,
            Reduction = System.Numerics.BitOperations.Log2((uint)Scale),
        };
        if (Decode() == 0)
        {
            throw new InvalidOperationException("The photograph decoded to nothing.");
        }
    }

    [Benchmark]
    public int Decode()
    {
        using DecodedImage image = SkiaImageDecoder.Decode(_jpeg, _request, CancellationToken.None)!;
        return image.DecodedRows;
    }
}
