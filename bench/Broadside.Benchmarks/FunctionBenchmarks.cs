using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Benchmarks;

/// <summary>
/// Evaluates functions (ISO 32000-2 §7.10) 1,000 times each, as a shading or tint conversion does per pixel. Evaluation is a hot
/// path: <c>Allocated</c> must read <c>-</c>; <c>Broadside.Tests.Graphics.FunctionAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class FunctionBenchmarks
{
    private const int Points = 1_000;

    private static readonly Dictionary<string, Func<CosObject>> Functions = new()
    {
        ["Type4LogoGreen"] = static () => Program(
            "<< /FunctionType 4 /Domain [0 1] /Range [0 1 0 1 0 1 0 1] >>",
            "{dup 0.84 mul exch 0.00 exch dup 0.44 mul exch 0.21 mul}"),
        ["Type4DoubleDot"] = static () => Program(
            "<< /FunctionType 4 /Domain [-1 1 -1 1] /Range [-1 1] >>",
            "{360 mul sin 2 div exch 360 mul sin 2 div add}"),
        ["Type4Hexachrome"] = static () => Program(
            "<< /FunctionType 4 /Domain [0 1 0 1 0 1 0 1 0 1 0 1] /Range [0 1 0 1 0 1 0 1] >>",
            "{ 5 index 1 index 0.6 mul add 5 index 3 index 0.5 mul add 5 index 4 index 0.9 mul add 3 index 0.8 mul add " +
            "5 index 10 4 roll pop pop pop pop pop pop dup 0.99 gt { pop 1 } if }"),
        ["Type0OneInThreeOut"] = static () => new CosStream(
            Dictionary("<< /FunctionType 0 /Domain [0 1] /Range [0 1 0 1 0 1] /Size [256] /BitsPerSample 8 >>"),
            Enumerable.Range(0, 768).Select(static i => (byte)(i * 7)).ToArray()),
        ["Type0TwoIn"] = static () => new CosStream(
            Dictionary("<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] /Size [16 16] /BitsPerSample 8 >>"),
            Enumerable.Range(0, 768).Select(static i => (byte)(i * 11)).ToArray()),
        ["Type2"] = static () => Dictionary("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 0.5 0.25] /N 1.7 >>"),
        ["Type3"] = static () => Dictionary(
            "<< /FunctionType 3 /Domain [0 1] /Bounds [0.25 0.5 0.75] /Encode [0 1 1 0 0 1 1 0] /Functions [" +
            "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 1] /C1 [1 0 0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0 1 0] /C1 [1 1 1] /N 2 >> " +
            "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 1 0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 3 >>] >>"),
    };

    private PdfDocument? _document;
    private PdfFunction? _function;
    private float[] _inputs = [];
    private float[] _outputs = [];

    /// <summary>The function to evaluate.</summary>
    [Params("Type4LogoGreen", "Type4DoubleDot", "Type4Hexachrome", "Type0OneInThreeOut", "Type0TwoIn", "Type2", "Type3")]
    public string Function { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Create();
        _function = _document.GetFunction(Functions[Function]()) ?? throw new InvalidOperationException("Not a function.");
        if (!_function.IsValid || _document.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"{Function} does not compile cleanly.");
        }

        int m = _function.InputCount;
        _inputs = new float[Points * m];
        for (int i = 0; i < _inputs.Length; i++)
        {
            _inputs[i] = (i * 0.618034f) % 1f;
        }

        _outputs = new float[Points * _function.OutputCount];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>One evaluation per call, 1,000 points.</summary>
    [Benchmark(Baseline = true)]
    public float EvaluateEach()
    {
        PdfFunction function = _function!;
        int m = function.InputCount;
        int n = function.OutputCount;
        for (int k = 0; k < Points; k++)
        {
            function.Evaluate(_inputs.AsSpan(k * m, m), _outputs.AsSpan(k * n, n));
        }

        return _outputs[^1];
    }

    /// <summary>The same 1,000 points as one batch.</summary>
    [Benchmark]
    public float EvaluateBatch()
    {
        _function!.Evaluate(_inputs, _outputs, Points);
        return _outputs[^1];
    }

    private static CosDictionary Dictionary(string syntax) => (CosDictionary)CosObject.Parse(Encoding.ASCII.GetBytes(syntax));

    private static CosStream Program(string dictionary, string code) => new(Dictionary(dictionary), Encoding.ASCII.GetBytes(code));
}
