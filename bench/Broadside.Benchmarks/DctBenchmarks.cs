using System.Buffers;
using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
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
