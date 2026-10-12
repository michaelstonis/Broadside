using System.Text;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Tests.Graphics;

/// <summary>Builds function objects from PDF syntax and evaluates them through the public <see cref="PdfFunction"/> view.</summary>
internal static class FunctionTesting
{
    /// <summary>Parses one COS object written in PDF syntax.</summary>
    public static CosObject Cos(string syntax) => CosObject.Parse(Encoding.Latin1.GetBytes(syntax));

    /// <summary>A stream with <paramref name="dictionary"/> and the given (unfiltered) data.</summary>
    public static CosStream Stream(string dictionary, byte[] data) => new((CosDictionary)Cos(dictionary), data);

    /// <summary>A Type 4 function stream: <paramref name="dictionary"/> plus the program text.</summary>
    public static CosStream Program(string dictionary, string code) => Stream(dictionary, Encoding.ASCII.GetBytes(code));

    /// <summary>A Type 4 function of <paramref name="inputs"/> inputs in [-1000 1000] and <paramref name="outputs"/> outputs in [-10^10 10^10].</summary>
    public static CosStream Calculator(string code, int inputs, int outputs) =>
        Program($"<< /FunctionType 4 /Domain [{Pairs(inputs, "-1000 1000")}] /Range [{Pairs(outputs, "-10000000000 10000000000")}] >>", code);

    /// <summary>The view of <paramref name="value"/>; fails the test when the document does not see a function.</summary>
    public static PdfFunction Function(this PdfDocument document, CosObject value) =>
        document.GetFunction(value) ?? throw new InvalidOperationException("Not a function.");

    /// <summary>Evaluates <paramref name="function"/> at one point.</summary>
    public static float[] At(this PdfFunction function, params float[] input)
    {
        float[] output = new float[function.OutputCount];
        function.Evaluate(input, output);
        return output;
    }

    /// <summary>Asserts that two output vectors agree to within <paramref name="tolerance"/>.</summary>
    public static void Near(float[] expected, float[] actual, double tolerance = 1e-5)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(
                Math.Abs(expected[i] - actual[i]) <= tolerance,
                $"Output {i}: expected {expected[i]}, got {actual[i]} (all: [{string.Join(", ", actual)}]).");
        }
    }

    /// <summary>The codes of the document's diagnostics, in order.</summary>
    public static string[] Codes(this PdfDocument document) => [.. document.Diagnostics.Select(static d => d.Code)];

    private static string Pairs(int count, string pair) => string.Join(" ", Enumerable.Repeat(pair, count));
}
