using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Converts a 1-megapixel image's worth of 8-bit samples through the ICC engine (ISO 32000-2 §8.6.5.5; ICC.1:2022 Annex F.2, F.3),
/// as an image decoder converts rows: the matrix/TRC RGB profile (parametric curves) and the monochrome profile of
/// <c>icc-rgb-matrix.pdf</c>, to 8-bit RGB. Conversion is a hot path: <c>Allocated</c> must read <c>-</c>;
/// <c>Broadside.Rendering.Tests.Icc.IccAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class IccConversionBenchmarks
{
    private const int Pixels = 1 << 20;

    private PdfDocument? _document;
    private PdfColorConverter? _converter;
    private byte[] _samples = [];
    private byte[] _bytes = [];

    /// <summary>The ColorSpace resource of the corpus file: <c>CS0</c> (RGB, parametric curves) or <c>CS2</c> (gray, the sRGB curve).</summary>
    [Params("CS0", "CS2")]
    public string Space { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "icc-rgb-matrix.pdf"), new PdfOptions().UseIccColorManagement());
        var resources = (CosDictionary)_document.Pages[0].Resources!;
        var spaces = (CosDictionary)_document.Resolve(resources[new CosName("ColorSpace")]);
        _converter = _document.GetColorConverter(_document.GetColorSpace(spaces[new CosName(Space)]));
        if (_document.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"{Space} does not read cleanly.");
        }

        // Deterministic pseudo-random samples (a multiplicative hash), so every run converts the same data.
        _samples = new byte[Pixels * _converter.InputCount];
        for (int i = 0; i < _samples.Length; i++)
        {
            _samples[i] = (byte)(unchecked((uint)i * 2654435761u) >> 24);
        }

        _bytes = new byte[Pixels * 3];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>A megapixel of 8-bit samples to 8-bit RGB.</summary>
    [Benchmark]
    public int ConvertBytes()
    {
        _converter!.Convert(_samples, _bytes, Pixels);
        return _bytes[^1];
    }
}
