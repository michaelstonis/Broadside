using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering.CoreGraphics;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #375: decoding a JPEG with ImageIO (<see cref="ImageIODecoder"/>) against the managed DCT decoder, at full size and at each
/// reduction (ISO 32000-2 §7.4.8, §8.9.4). ImageIO's reductions are <c>kCGImageSourceSubsampleFactor</c>, which the decoder never
/// uses (it is outside the tolerance); they are measured here to show why. The photographs are <see cref="CatalogueSamples"/>'s:
/// the 2550 x 3150 4:4:4 cover at quality 90 (which ImageIO takes) and a 5280 x 3792 4:2:0 photograph (which it does not: its chroma
/// is subsampled). Results are in <c>docs/benchmarks/imageio-image-decoder.md</c>.
/// </summary>
[MemoryDiagnoser]
public class ImageIODecodeBenchmarks
{
    private byte[] _jpeg = [];
    private PlatformImageRequest _request;

    /// <summary>The photograph.</summary>
    [Params("cover-444", "photo-420")]
    public string Image { get; set; } = "";

    /// <summary>The reduction, as a power of two: 0 for full size, 3 for an eighth.</summary>
    [Params(0, 1, 2, 3)]
    public int Reduction { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (int width, int height, DctChromaSubsampling subsampling) = Image == "cover-444"
            ? (2550, 3150, DctChromaSubsampling.None)
            : (5280, 3792, DctChromaSubsampling.HorizontalAndVertical);
        _jpeg = CatalogueSamples.Jpeg(width, height, subsampling);
        _request = new PlatformImageRequest
        {
            Filter = new CosName("DCTDecode"),
            Width = width,
            Height = height,
            Components = 3,
            BitsPerComponent = 8,
            ColorTransform = true,
            ChromaSubsampled = subsampling != DctChromaSubsampling.None,
            Reduction = Reduction,
        };
    }

    /// <summary>The managed DCT decoder at the reduction.</summary>
    [Benchmark(Baseline = true)]
    public int Managed()
    {
        using DecodedImage? image = new DctDecodeFilter().DecodeImage(_jpeg, new ImageFilterContext(new FilterContext())
        {
            Width = _request.Width,
            Height = _request.Height,
            BitsPerComponent = 8,
            ColorComponents = 3,
            Reduction = Reduction,
        });
        return image!.Width;
    }

    /// <summary>ImageIO at the reduction (<c>kCGImageSourceSubsampleFactor</c> above 0), repacked into the core's samples.</summary>
    [Benchmark]
    public int ImageIO()
    {
        using DecodedImage? image = ImageIODecoder.Decode(_jpeg, _request, Reduction);
        return image!.Width;
    }
}
