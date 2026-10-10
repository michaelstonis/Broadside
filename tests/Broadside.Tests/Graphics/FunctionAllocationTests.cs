using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Function evaluation is a hot path (every shading pixel, every tint) and allocates nothing (CLAUDE.md "Code conventions";
/// ISO 32000-2 §7.10). This is the build-breaking half of that rule; <c>FunctionBenchmarks</c> is the measuring half. In the heavy
/// collection, like the other allocation tests that run many iterations, so tiered compilation triggered by parallel tests does not
/// show up in this thread's count.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class FunctionAllocationTests
{
    private const int Evaluations = 10_000;

    public static TheoryData<string> Functions => [.. Samples.Keys];

    private static Dictionary<string, Func<CosObject>> Samples { get; } = new()
    {
        ["Type 0, 1 in, 3 out, 8-bit"] = static () => Stream(
            "<< /FunctionType 0 /Domain [0 1] /Range [0 1 0 1 0 1] /Size [4] /BitsPerSample 8 >>",
            [.. Enumerable.Range(0, 12).Select(static i => (byte)(i * 20))]),
        ["Type 0, 2 in, 1 out, 16-bit"] = static () => Stream(
            "<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [3 3] /BitsPerSample 16 >>",
            [.. Enumerable.Range(0, 18).Select(static i => (byte)(i * 13))]),
        ["Type 2"] = static () => Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 0.5 0.25] /N 1.7 >>"),
        ["Type 3"] = static () => Cos(
            "<< /FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 1 0] /Functions [" +
            "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 1] /C1 [1 0 0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0 1 0] /C1 [1 1 1] /N 2 >>] >>"),
        ["Type 4, LogoGreen"] = static () => Program(
            "<< /FunctionType 4 /Domain [0 1] /Range [0 1 0 1 0 1 0 1] >>",
            "{dup 0.84 mul exch 0.00 exch dup 0.44 mul exch 0.21 mul}"),
        ["Type 4, DoubleDot"] = static () => Program(
            "<< /FunctionType 4 /Domain [-1 1 -1 1] /Range [-1 1] >>",
            "{360 mul sin 2 div exch 360 mul sin 2 div add}"),
        ["Type 4, hexachrome"] = static () => Program(
            "<< /FunctionType 4 /Domain [0 1 0 1 0 1 0 1 0 1 0 1] /Range [0 1 0 1 0 1 0 1] >>",
            HexachromeProgram),
    };

    /// <summary>
    /// A 6-in 4-out hexachrome tint transform (§7.10.5.1 NOTE 1): c m y k orange green to C = c + 0.6 green, M = m + 0.5 orange,
    /// Y = y + 0.9 orange + 0.8 green, K = k (snapped to 1 above 0.99).
    /// </summary>
    internal const string HexachromeProgram =
        "{ 5 index 1 index 0.6 mul add 5 index 3 index 0.5 mul add 5 index 4 index 0.9 mul add 3 index 0.8 mul add " +
        "5 index 10 4 roll pop pop pop pop pop pop dup 0.99 gt { pop 1 } if }";

    [Theory]
    [MemberData(nameof(Functions))]
    public void Evaluating_a_function_allocates_nothing(string name)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Samples[name]());
        float[] input = new float[function.InputCount];
        float[] output = new float[function.OutputCount];
        float[] inputs = new float[function.InputCount * 16];
        float[] outputs = new float[function.OutputCount * 16];
        Run(function, input, output, inputs, outputs, 1_000);

        long allocated = Allocations.Measure(() => Run(function, input, output, inputs, outputs, Evaluations), warmUpCalls: 0);

        Assert.True(function.IsValid);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(0, allocated);
    }

    private static void Run(PdfFunction function, float[] input, float[] output, float[] inputs, float[] outputs, int count)
    {
        for (int k = 0; k < count; k++)
        {
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = ((k * (i + 3)) % 101) / 100f;
            }

            function.Evaluate(input, output);
        }

        for (int k = 0; k < count / 16; k++)
        {
            function.Evaluate(inputs, outputs, 16);
        }
    }
}
