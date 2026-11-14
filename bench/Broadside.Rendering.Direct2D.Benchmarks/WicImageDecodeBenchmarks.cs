using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering.Direct2D;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #377 (ISO 32000-2 §7.4.8): decoding the 8-megapixel catalogue-class photograph (2550 x 3150, 4:4:4, and the same at 4:2:0)
/// at each power-of-two reduction with the managed decoder and with <see cref="WicImageDecoder"/> (WIC's own pixels at 4:4:4, its
/// YCbCr planes put through the managed transform at 4:2:0; both bit-identical). The samples land in pooled buffers. Windows-only;
/// CI dry-runs it on the Windows leg, and <c>WicImageDecoderTests</c> gates the managed-to-WIC ratio at full size.
/// </summary>
[MemoryDiagnoser]
public class WicImageDecodeBenchmarks
{
    private byte[] _jpeg = [];
    private PlatformImageRequest _request;

    /// <summary>The chroma subsampling of the photograph.</summary>
    [Params("4:4:4", "4:2:0")]
    public string Sampling { get; set; } = "4:4:4";

    /// <summary>The reduction asked for: 1, 2, 4 or 8.</summary>
    [Params(1, 2, 4, 8)]
    public int Scale { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        _jpeg = CatalogueSamples.Jpeg(2550, 3150, Sampling == "4:4:4" ? DctChromaSubsampling.None : DctChromaSubsampling.HorizontalAndVertical);
        _request = new PlatformImageRequest
        {
            Filter = new CosName("DCTDecode"),
            Width = 2550,
            Height = 3150,
            Components = 3,
            BitsPerComponent = 8,
            ColorTransform = true,
            ChromaSubsampled = Sampling != "4:4:4",
            Reduction = System.Numerics.BitOperations.Log2((uint)Scale),
        };
    }

    [Benchmark(Baseline = true)]
    public int Managed()
    {
        using DecodedImage? image = new DctDecodeFilter().DecodeImage(_jpeg, new ImageFilterContext(new FilterContext())
        {
            Geometry = new ImageGeometry(2550, 3150, 3, 8),
            Reduction = _request.Reduction,
        });
        return image!.Geometry.Width;
    }

    [Benchmark]
    public int Wic()
    {
        // WIC decodes synchronously: the task has completed when DecodeAsync returns.
        ValueTask<DecodedImage?> decode = WicImageDecoder.Shared.DecodeAsync(_jpeg, _request, CancellationToken.None);
        using DecodedImage? image = decode.IsCompleted ? decode.Result : throw new InvalidOperationException("The WIC decode did not complete synchronously.");
        return image!.Geometry.Width;
    }
}
