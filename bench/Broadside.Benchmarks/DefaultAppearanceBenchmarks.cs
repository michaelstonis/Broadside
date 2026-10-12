using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Forms;

namespace Broadside.Benchmarks;

/// <summary>
/// Parses default appearance strings (ISO 32000-2 §12.7.4.3, #166). Tokenizing allocates nothing beyond the result, so
/// <c>Allocated</c> is the same for the short and the long string, whose results have the same shape;
/// <c>Broadside.Tests.Forms.DefaultAppearanceAllocationTests</c> fails the build if it is not.
/// </summary>
[MemoryDiagnoser]
public class DefaultAppearanceBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>The string: a typical one, and one with a comment and 200 overridden colour operators before the same result.</summary>
    [Params("plain", "noisy")]
    public string Kind { get; set; } = "";

    [GlobalSetup]
    public void Setup() => _bytes = Kind == "plain"
        ? "/Helv 12 Tf 0 0 1 rg 2 Tc"u8.ToArray()
        : Encoding.Latin1.GetBytes("% a comment\n/F1 9 Tf 1 g " + string.Concat(Enumerable.Repeat("0 g  1 0 0 rg\r\n", 200)) + "/Helv 12 Tf 0 0 1 rg 2 Tc");

    [Benchmark]
    public PdfDefaultAppearance Parse() => PdfDefaultAppearance.Parse(_bytes);
}
