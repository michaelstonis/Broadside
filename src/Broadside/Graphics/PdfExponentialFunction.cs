using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>A Type 2 (exponential interpolation) function (PDF 1.3): y_j = C0_j + x^N × (C1_j − C0_j).</summary>
/// <remarks>ISO 32000-2 §7.10.3, Table 40. One input; n outputs, n being the length of <see cref="C0"/> and <see cref="C1"/>.</remarks>
public sealed class PdfExponentialFunction : PdfFunction
{
    internal PdfExponentialFunction(FunctionCache cache, CosObject cosObject, CosReference? reference, CompiledFunction compiled, int expectedInputs, int expectedOutputs)
        : base(cache, cosObject, reference, compiled, expectedInputs, expectedOutputs)
    {
    }

    /// <inheritdoc/>
    public override PdfFunctionType FunctionType => PdfFunctionType.Exponential;

    /// <summary>Gets the <c>C0</c> entry, the outputs at x = 0; [0.0] when absent.</summary>
    /// <remarks>ISO 32000-2 §7.10.3, Table 40.</remarks>
    public IReadOnlyList<double> C0 => ReadNumbers(FunctionNames.C0) ?? [0.0];

    /// <summary>Gets the <c>C1</c> entry, the outputs at x = 1; [1.0] when absent.</summary>
    /// <remarks>ISO 32000-2 §7.10.3, Table 40.</remarks>
    public IReadOnlyList<double> C1 => ReadNumbers(FunctionNames.C1) ?? [1.0];

    /// <summary>Gets the <c>N</c> entry, the interpolation exponent; <see langword="null"/> when missing (the function is then invalid).</summary>
    /// <remarks>ISO 32000-2 §7.10.3, Table 40.</remarks>
    public double? Exponent => ReadNumber(FunctionNames.N);
}
