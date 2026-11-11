using System.Buffers;
using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Filters.Dct;
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

/// <summary>
/// The DCT colour transforms on one row (ISO 32000-2 §7.4.8, Table 13; issue #471): YCbCr to RGB and YCCK to CMYK, one sample at a
/// time (<c>Scalar</c>, the code before #471) and through the dispatcher (the <c>Vector128</c> path where it is accelerated), over a
/// 2,550-sample row (the width of the catalogue cover's photograph). <c>Allocated</c> must read <c>-</c>; the vector path's
/// bit-exactness is <c>Broadside.Tests.Filters.Dct.JpegColorKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class JpegColorBenchmarks
{
    private const int Width = 2550;
    private readonly byte[] _y = new byte[Width];
    private readonly byte[] _cb = new byte[Width];
    private readonly byte[] _cr = new byte[Width];
    private readonly byte[] _k = new byte[Width];
    private readonly byte[] _output = new byte[4 * Width];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(471);
        random.NextBytes(_y);
        random.NextBytes(_cb);
        random.NextBytes(_cr);
        random.NextBytes(_k);
#pragma warning restore CA5394
    }

    [Benchmark]
    public byte YccToRgbScalar()
    {
        JpegColor.YccToRgbScalar(_y, _cb, _cr, _output.AsSpan(0, 3 * Width));
        return _output[0];
    }

    [Benchmark]
    public byte YccToRgb()
    {
        JpegColor.YccToRgb(_y, _cb, _cr, _output.AsSpan(0, 3 * Width));
        return _output[0];
    }

    [Benchmark]
    public byte YcckToCmykScalar()
    {
        JpegColor.YcckToCmykScalar(_y, _cb, _cr, _k, _output);
        return _output[0];
    }

    [Benchmark]
    public byte YcckToCmyk()
    {
        JpegColor.YcckToCmyk(_y, _cb, _cr, _k, _output);
        return _output[0];
    }
}

/// <summary>
/// The 8 x 8 inverse DCT (ITU-T T.81 §A.3.3; issue #474) over 1,024 blocks shaped like a quantized photograph (low frequencies more
/// often non-zero, so libjpeg's column and row shortcuts are taken), with the T.81 Table K.1 luminance table: one block at a time
/// (<c>Scalar</c>, the code before #474) and through the dispatcher (the <c>Vector128</c> path where it is accelerated).
/// <c>Allocated</c> must read <c>-</c>; the vector path's bit-exactness is <c>Broadside.Tests.Filters.Dct.InverseDctKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class InverseDctBenchmarks
{
    private const int Blocks = 1024;
    private readonly short[] _coefficients = new short[64 * Blocks];
    private readonly byte[] _samples = new byte[64 * Blocks];
    private readonly int[] _quantization =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(474);
        for (int i = 0; i < _coefficients.Length; i++)
        {
            int position = i % 64;
            int frequency = (position >> 3) + (position & 7);
            _coefficients[i] = random.Next(256) < 256 >> Math.Min(frequency, 8) ? (short)(random.Next(-128, 128) >> 2) : (short)0;
        }
#pragma warning restore CA5394
    }

    [Benchmark]
    public byte TransformScalar()
    {
        for (int i = 0; i < Blocks; i++)
        {
            InverseDct.TransformScalar(_coefficients.AsSpan(64 * i, 64), _quantization, _samples.AsSpan(64 * i, 64), 8);
        }

        return _samples[0];
    }

    [Benchmark]
    public byte Transform()
    {
        for (int i = 0; i < Blocks; i++)
        {
            InverseDct.Transform(_coefficients.AsSpan(64 * i, 64), _quantization, _samples.AsSpan(64 * i, 64), 8);
        }

        return _samples[0];
    }
}
