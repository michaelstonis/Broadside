using System.Buffers;
using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// DCTDecode (ISO 32000-2 §7.4.8; ITU-T T.81) over the test vectors of <c>tests/Broadside.Tests/Filters/Dct/</c>: 4:2:0 and 4:4:4
/// YCbCr, one component non-interleaved, one scan per component (the buffered coefficient path), a restart interval, progressive
/// Huffman and progressive arithmetic coding (issue #62) and YCCK to CMYK.
/// <see cref="Decode"/> is the plain filter path into a reused buffer: the decoder is a hot path (entropy decoding, IDCT, colour
/// conversion) and <c>Allocated</c> must read <c>-</c>; <c>Broadside.Tests.Filters.Dct.DctAllocationTests</c> fails the build
/// otherwise. <see cref="DecodeImage"/> is the image facet, which allocates the <see cref="DecodedImage"/> per image.
/// </summary>
[MemoryDiagnoser]
public class DctBenchmarks
{
    private readonly ArrayBufferWriter<byte> _output = new(256 * 1024);
    private readonly FilterContext _context = new();
    private readonly DctDecodeFilter _filter = new();
    private byte[] _jpeg = [];

    /// <summary>The vector: a file name without <c>.jpg</c>.</summary>
    [Params("testorig", "sampling-444", "gray-2x2", "multiscan", "restart-blocks", "progressive", "arithmetic-progressive", "ycck")]
    public string Vector { get; set; } = "testorig";

    [GlobalSetup]
    public void Setup()
    {
        _jpeg = File.ReadAllBytes(Path.Combine(CorpusLocator.RepositoryRoot, "tests", "Broadside.Tests", "Filters", "Dct", Vector + ".jpg"));
        if (Decode() == 0)
        {
            throw new InvalidOperationException($"{Vector} decoded to nothing.");
        }
    }

    [Benchmark]
    public int Decode()
    {
        _output.ResetWrittenCount();
        _filter.Decode(_jpeg, _output, _context);
        return _output.WrittenCount;
    }

    [Benchmark]
    public int DecodeImage()
    {
        using DecodedImage image = _filter.DecodeImage(_jpeg, new ImageFilterContext(_context))!;
        return image.DecodedRows;
    }
}

/// <summary>
/// The DCT baseline encoder (ISO 32000-2 §7.4.8; ITU-T T.81 Annex A, F.1 and K; issue #143) through the encoder contract into a
/// reused buffer, at quality 90: testorig (227 x 149) as YCbCr 4:2:0 and 4:4:4 and its red channel as gray, and the 75 x 43 CMYK
/// vector untransformed and as YCCK 4:2:0. Colour conversion, downsampling, FDCT, quantization and Huffman coding are a hot path:
/// <c>Allocated</c> must read <c>-</c> (the per-image buffers are pooled); <c>Broadside.Tests.Filters.Dct.DctAllocationTests</c>
/// fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class DctEncoderBenchmarks
{
    private readonly ArrayBufferWriter<byte> _output = new(256 * 1024);
    private byte[] _samples = [];
    private DctEncoder _encoder = new();
    private StreamEncoderContext _context = new();

    /// <summary>The image and layout.</summary>
    [Params("rgb-420", "rgb-444", "gray", "cmyk", "ycck-420")]
    public string Layout { get; set; } = "rgb-420";

    [GlobalSetup]
    public void Setup()
    {
        bool cmyk = Layout is "cmyk" or "ycck-420";
        byte[] jpeg = File.ReadAllBytes(Path.Combine(CorpusLocator.RepositoryRoot, "tests", "Broadside.Tests", "Filters", "Dct", cmyk ? "cmyk.jpg" : "testorig.jpg"));
        var decoded = new ArrayBufferWriter<byte>();
        new DctDecodeFilter().Decode(jpeg, decoded, new FilterContext());
        (int width, int height) = cmyk ? (75, 43) : (227, 149);
        _samples = Layout == "gray" ? [.. decoded.WrittenSpan.ToArray().Where((_, i) => i % 3 == 0)] : decoded.WrittenSpan.ToArray();
        _encoder = new DctEncoder(90, Layout.EndsWith("-420", StringComparison.Ordinal) ? DctChromaSubsampling.HorizontalAndVertical : DctChromaSubsampling.None);
        _context = new StreamEncoderContext
        {
            StreamDictionary = new CosDictionary { [new CosName("Width")] = new CosInteger(width), [new CosName("Height")] = new CosInteger(height) },
            Parameters = Layout == "ycck-420" ? new CosDictionary { [new CosName("ColorTransform")] = new CosInteger(1) } : null,
        };
        if (Encode() == 0)
        {
            throw new InvalidOperationException($"{Layout} encoded to nothing.");
        }
    }

    [Benchmark]
    public int Encode()
    {
        _output.ResetWrittenCount();
        _encoder.Encode(_samples, _output, _context);
        return _output.WrittenCount;
    }
}
