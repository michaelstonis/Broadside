using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Converts a 1-megapixel CMYK image's worth of 8-bit samples through a four-input lookup table (ISO 32000-2 §8.6.5.5; ICC.1:2022
/// §10.10 lut16Type, §10.12 lutAToBType), as an image decoder converts rows, to 8-bit RGB: <c>CS0</c> (lut16Type, 256-entry input
/// tables, 9-point CLUT, legacy PCSLAB) and <c>CS1</c> (lutAToBType, identity A curves, 16-bit 9-point CLUT, B curves) of
/// <c>icc-lut.pdf</c>. Target: at most 50 ms per megapixel on Apple silicon (#118 measured 42 ms for CS0 and 33 ms for CS1 in a
/// full run). Conversion is a hot path: <c>Allocated</c> must read <c>-</c>; <c>Broadside.Rendering.Tests.Icc.IccAllocationTests</c>
/// fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class IccLutConversionBenchmarks
{
    private const int Pixels = 1 << 20;

    private PdfDocument? _document;
    private PdfColorConverter? _converter;
    private byte[] _samples = [];
    private byte[] _bytes = [];

    /// <summary>The ColorSpace resource of the corpus file, or <c>OutputIntent</c>: DeviceCMYK through the output intent of <c>output-intent-cmyk.pdf</c> (#119).</summary>
    [Params("CS0", "CS1", "OutputIntent")]
    public string Space { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        if (Space == "OutputIntent")
        {
            // Issue #119: a lut16Type output profile with black point compensation folded into the PCS step.
            _document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "output-intent-cmyk.pdf"), new PdfOptions().UseIccColorManagement());
            _converter = _document.GetColorConverter(PdfDeviceCmykColorSpace.Instance, new ColorConversion { OutputIntent = _document.OutputIntents[0] });
        }
        else
        {
            _document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "icc-lut.pdf"), new PdfOptions().UseIccColorManagement());
            var resources = (CosDictionary)_document.Pages[0].Resources!;
            var spaces = (CosDictionary)_document.Resolve(resources[new CosName("ColorSpace")]);
            _converter = _document.GetColorConverter(_document.GetColorSpace(spaces[new CosName(Space)]));
        }

        if (_document.Diagnostics.Count > 0 || _converter.InputCount != 4)
        {
            throw new InvalidOperationException($"{Space} does not read cleanly as CMYK.");
        }

        // Deterministic pseudo-random samples (a multiplicative hash), so every run converts the same data.
        _samples = new byte[Pixels * 4];
        for (int i = 0; i < _samples.Length; i++)
        {
            _samples[i] = (byte)(unchecked((uint)i * 2654435761u) >> 24);
        }

        _bytes = new byte[Pixels * 3];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>A megapixel of 8-bit CMYK samples to 8-bit RGB.</summary>
    [Benchmark]
    public int ConvertCmykBytes()
    {
        _converter!.Convert(_samples, _bytes, Pixels);
        return _bytes[^1];
    }
}
