using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Benchmarks;

/// <summary>
/// The image row helpers over 256 rows of a 4096-sample RGB image at each depth (ISO 32000-2 §8.9.3, §8.9.5.2, §8.9.6.4,
/// §11.6.5.2), and the inline-image end finder over a 64 KB content stream of inline images (§8.9.7). Hot paths: <c>Allocated</c>
/// must read <c>-</c>; <c>Broadside.Tests.Images.ImageAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class ImageUnpackBenchmarks
{
    private const int Width = 4096;
    private const int Rows = 256;
    private const int Components = 3;

    private readonly OperandArena _arena = new();
    private byte[] _samples = [];
    private int _stride;
    private byte[] _bytes = [];
    private ushort[] _values = [];
    private float[] _colours = [];
    private float[] _alpha = [];
    private byte[] _coverage = [];
    private ImageDecodeMap? _map;
    private byte[] _content = [];

    private static readonly double[] KeyRanges = [10, 200, 0, 255, 30, 40];
    private static readonly float[] Matte = [1, 1, 1];
    private static readonly ComponentRange[] Ranges = [new(0, 1), new(0, 1), new(0, 1)];

    /// <summary>The bits per component.</summary>
    [Params(1, 2, 4, 8, 16)]
    public int Bits { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _stride = ((Width * Components * Bits) + 7) / 8;
        _samples = [.. Enumerable.Range(0, _stride * Rows).Select(static i => (byte)((i * 31) ^ (i >> 7)))];
        _bytes = new byte[Width * Components];
        _values = new ushort[Width * Components];
        _colours = new float[Width * Components];
        _alpha = [.. Enumerable.Range(0, Width).Select(static i => (i % 255) / 255f)];
        _coverage = new byte[Width];
        _map = ImageDecodeMap.Create([1, 0, 0.1, 0.9, 0, 1], Components, Bits);

        var content = new StringBuilder();
        while (content.Length < 64 * 1024)
        {
            content.Append("q 10 0 0 10 0 0 cm BI /W 16 /H 1 /CS /G /BPC 8 ID 0123456789EI Q AB EI Q\n");
            content.Append("q BI /W 8 /H 1 /CS /G /BPC 8 /F /RL ID abc EI xyz EI 1 0 0 1 2 3 cm Q\n");
            content.Append("q BI /W 2 /H 1 /CS /G /BPC 8 /F /A85 ID 87cURD]i,\"Ebo80~> EI Q\n");
        }

        _content = Encoding.Latin1.GetBytes(content.ToString());
    }

    [Benchmark]
    public int Unpack()
    {
        int total = 0;
        for (int y = 0; y < Rows; y++)
        {
            ReadOnlySpan<byte> row = _samples.AsSpan(y * _stride, _stride);
            if (Bits <= 8)
            {
                ImageRows.Unpack(row, Bits, Width * Components, _bytes);
                total += _bytes[^1];
            }
            else
            {
                ImageRows.Unpack(row, Bits, Width * Components, _values);
                total += _values[^1];
            }
        }

        return total;
    }

    [Benchmark]
    public int UnpackScaled()
    {
        int total = 0;
        for (int y = 0; y < Rows; y++)
        {
            ImageRows.UnpackScaled(_samples.AsSpan(y * _stride, _stride), Bits, Bits, Width * Components, _bytes);
            total += _bytes[^1];
        }

        return total;
    }

    [Benchmark]
    public float ApplyDecode()
    {
        float total = 0;
        for (int y = 0; y < Rows; y++)
        {
            ImageRows.Unpack(_samples.AsSpan(y * _stride, _stride), Bits, Width * Components, _values);
            _map!.Map(_values, _colours);
            total += _colours[^1];
        }

        return total;
    }

    [Benchmark]
    public int ColorKeyRow()
    {
        int total = 0;
        for (int y = 0; y < Rows; y++)
        {
            ImageRows.Unpack(_samples.AsSpan(y * _stride, _stride), Bits, Width * Components, _values);
            ImageRows.ColorKey(_values, Components, KeyRanges, _coverage);
            total += _coverage[^1];
        }

        return total;
    }

    [Benchmark]
    public float MatteRow()
    {
        float total = 0;
        for (int y = 0; y < Rows; y++)
        {
            ImageRows.Unpack(_samples.AsSpan(y * _stride, _stride), Bits, Width * Components, _values);
            _map!.Map(_values, _colours);
            ImageRows.Unpremultiply(_colours, Components, _alpha, Matte, Ranges);
            total += _colours[^1];
        }

        return total;
    }

    [Benchmark]
    public int InlineImageEndFinder()
    {
        var reader = new ContentReader(_content, _arena);
        int images = 0;
        while (reader.Next(out ReadOperator op))
        {
            images += op.Code == ContentOperatorCode.BeginInlineImage ? 1 : 0;
            _arena.Clear();
        }

        _arena.Clear();
        return images;
    }
}
