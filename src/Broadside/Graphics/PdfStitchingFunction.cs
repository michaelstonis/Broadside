using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>A Type 3 (stitching) function (PDF 1.3): k 1-input functions, each applied over its own subdomain of one domain.</summary>
/// <remarks>
/// ISO 32000-2 §7.10.4, Table 41. The subdomains are the intervals <see cref="Bounds"/> cuts the domain into; a value in subdomain i
/// is mapped through the i-th pair of <see cref="Encode"/> and passed to the i-th function of <see cref="Functions"/>.
/// </remarks>
public sealed class PdfStitchingFunction : PdfFunction
{
    internal PdfStitchingFunction(FunctionCache cache, CosObject cosObject, CosReference? reference, CompiledFunction compiled, int expectedInputs, int expectedOutputs)
        : base(cache, cosObject, reference, compiled, expectedInputs, expectedOutputs)
    {
    }

    /// <inheritdoc/>
    public override PdfFunctionType FunctionType => PdfFunctionType.Stitching;

    /// <summary>Gets the functions of the <c>Functions</c> entry, in order; an element that is not a function is <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.10.4, Table 41. Read from the dictionary on every call; each element's view is the document's shared one.</remarks>
    public IReadOnlyList<PdfFunction?> Functions
    {
        get
        {
            if (!Dictionary.TryGetValue(FunctionNames.Functions, out CosObject? value) || Cache.Resolve(value) is not CosArray array)
            {
                return [];
            }

            var functions = new PdfFunction?[array.Count];
            for (int i = 0; i < functions.Length; i++)
            {
                functions[i] = Cache.Get(array[i], expectedInputs: 1);
            }

            return functions;
        }
    }

    /// <summary>Gets the <c>Bounds</c> entry: k − 1 increasing numbers inside the domain; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §7.10.4, Table 41.</remarks>
    public IReadOnlyList<double> Bounds => ReadNumbers(FunctionNames.Bounds) ?? [];

    /// <summary>Gets the <c>Encode</c> entry: a pair per function mapping its subdomain onto the function's domain; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §7.10.4, Table 41.</remarks>
    public IReadOnlyList<double> Encode => ReadNumbers(FunctionNames.Encode) ?? [];
}
