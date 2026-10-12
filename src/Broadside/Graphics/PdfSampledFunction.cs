using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>A Type 0 (sampled) function (PDF 1.2): an m-dimensional table of samples with n values each, interpolated.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.2, Table 39. The samples are the stream's data: one bit stream of <see cref="BitsPerSample"/>-bit values with no
/// padding, the first input dimension varying fastest. Inputs are mapped through <see cref="Encode"/> into the table, and sample values
/// through <see cref="Decode"/> into the outputs.
/// </para>
/// <para>
/// Interpolation is multilinear between the 2^m surrounding samples (simplex interpolation between m + 1 of them when more than eight
/// inputs fall between samples). <see cref="Order"/> 3 (cubic spline) is evaluated linearly and noted once as a diagnostic.
/// Samples are held in single precision, so 24- and 32-bit samples resolve to about 2^-24 of their decoded range.
/// </para>
/// </remarks>
public sealed class PdfSampledFunction : PdfFunction
{
    internal PdfSampledFunction(FunctionCache cache, CosObject cosObject, CosReference? reference, CompiledFunction compiled, int expectedInputs, int expectedOutputs)
        : base(cache, cosObject, reference, compiled, expectedInputs, expectedOutputs)
    {
    }

    /// <inheritdoc/>
    public override PdfFunctionType FunctionType => PdfFunctionType.Sampled;

    /// <summary>Gets the stream holding the samples; <see langword="null"/> when the function object is a dictionary (the function is then invalid).</summary>
    public CosStream? Stream => CosObject as CosStream;

    /// <summary>Gets the <c>Size</c> entry: the number of samples in each input dimension; empty when missing.</summary>
    /// <remarks>ISO 32000-2 §7.10.2, Table 39.</remarks>
    public IReadOnlyList<double> Size => ReadNumbers(FunctionNames.Size) ?? [];

    /// <summary>Gets the <c>BitsPerSample</c> entry (1, 2, 4, 8, 12, 16, 24 or 32); <see langword="null"/> when missing.</summary>
    /// <remarks>ISO 32000-2 §7.10.2, Table 39.</remarks>
    public int? BitsPerSample => ReadNumber(FunctionNames.BitsPerSample) is { } bits ? (int)Math.Clamp(bits, int.MinValue, int.MaxValue) : null;

    /// <summary>Gets the <c>Order</c> entry: 1 (linear, the default) or 3 (cubic spline).</summary>
    /// <remarks>ISO 32000-2 §7.10.2, Table 39.</remarks>
    public int Order => ReadNumber(FunctionNames.Order) is { } order ? (int)Math.Clamp(order, int.MinValue, int.MaxValue) : 1;

    /// <summary>Gets the <c>Encode</c> entry; when absent, its default [0 (Size0 − 1) 0 (Size1 − 1) …].</summary>
    /// <remarks>ISO 32000-2 §7.10.2, Table 39.</remarks>
    public IReadOnlyList<double> Encode => ReadNumbers(FunctionNames.Encode) ?? [.. Size.SelectMany(static size => new[] { 0, size - 1 })];

    /// <summary>Gets the <c>Decode</c> entry; when absent, its default, the range (empty when that is missing too).</summary>
    /// <remarks>ISO 32000-2 §7.10.2, Table 39.</remarks>
    public IReadOnlyList<double> Decode => ReadNumbers(FunctionNames.Decode) ?? Range ?? [];
}
