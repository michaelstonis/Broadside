using BenchmarkDotNet.Attributes;

namespace Broadside.Benchmarks;

/// <summary>
/// PDF/A validation of the representative sample documents (samples/documents/pdf, issue #651): <see cref="PdfDocument.ValidatePdfA"/>
/// against PDF/A-2b on a document opened once in <see cref="Setup"/>, every page's content walked once. ISO 19005-2. Not a hot path
/// and not gated: memory is shown, not asserted.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases it in [GlobalCleanup].")]
public class PdfAValidationBenchmarks
{
    private PdfDocument? _document;

    /// <summary>Gets or sets the sample document's file name.</summary>
    [ParamsSource(nameof(Files))]
    public string Sample { get; set; } = string.Empty;

    /// <summary>Every sample document.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Files() => SampleDocuments.Files();

    [GlobalSetup]
    public void Setup()
    {
        (string file, byte[] bytes, int _) = SampleDocuments.Read($"{Sample}#0");
        _document = SampleDocuments.Open(file, bytes);
        _ = Validate();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public int Validate() => _document!.ValidatePdfA(PdfAConformance.PdfA2b).Violations.Count;
}
