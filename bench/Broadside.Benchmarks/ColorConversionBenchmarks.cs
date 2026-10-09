using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Benchmarks;

/// <summary>
/// Converts a 1-megapixel image's worth of colours (ISO 32000-2 §8.6, §10.4) through <see cref="PdfColorConverter"/>, as an image
/// decoder converts rows: 8-bit DeviceCMYK through the SWOP characterisation, an 8-bit Separation through its 256-entry table,
/// 8-bit Indexed through its palette, and Lab from floats through XYZ to sRGB. Conversion is a hot path: <c>Allocated</c> must
/// read <c>-</c>; <c>Broadside.Tests.Colors.ColorAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class ColorConversionBenchmarks
{
    private const int Pixels = 1 << 20;

    private static readonly Dictionary<string, string> Spaces = new()
    {
        ["Cmyk"] = "/DeviceCMYK",
        ["Separation"] = "[/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.4 1 0] /N 1 >>]",
        ["Indexed"] = "[/Indexed /DeviceRGB 255 <" + string.Concat(Enumerable.Range(0, 256).Select(static i => $"{i:X2}{255 - i:X2}{(i * 7) & 255:X2}")) + ">]",
        ["Lab"] = "[/Lab << /WhitePoint [0.9642 1 0.8249] /Range [-128 127 -128 127] >>]",
    };

    private PdfDocument? _document;
    private PdfColorConverter? _converter;
    private byte[] _samples = [];
    private byte[] _bytes = [];
    private float[] _components = [];
    private float[] _colors = [];

    /// <summary>The source colour space.</summary>
    [Params("Cmyk", "Separation", "Indexed", "Lab")]
    public string Space { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Create();
        PdfColorSpace space = _document.GetColorSpace(CosObject.Parse(Encoding.ASCII.GetBytes(Spaces[Space])));
        _converter = _document.GetColorConverter(space);
        if (_document.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"{Space} does not read cleanly.");
        }

        // Deterministic pseudo-random samples (a multiplicative hash), so every run converts the same data.
        _samples = new byte[Pixels * _converter.InputCount];
        _components = new float[Pixels * _converter.InputCount];
        for (int i = 0; i < _samples.Length; i++)
        {
            uint hash = unchecked((uint)i * 2654435761u);
            _samples[i] = (byte)(hash >> 24);
            _components[i] = (hash >> 8) / (float)(1 << 24) * 200 - 100;
        }

        _bytes = new byte[Pixels * 3];

        _colors = new float[Pixels * 3];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>A megapixel of 8-bit samples to 8-bit RGB (the Lab samples map 0..255 onto L* 0..100 and a*, b* -128..127).</summary>
    [Benchmark]
    public void ConvertBytes() => _converter!.Convert(_samples, _bytes, Pixels);

    /// <summary>A megapixel of float components to float RGB.</summary>
    [Benchmark]
    public void ConvertFloats() => _converter!.Convert(_components, _colors, Pixels);
}
