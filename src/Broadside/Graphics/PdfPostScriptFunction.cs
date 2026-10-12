using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>A Type 4 (PostScript calculator) function (PDF 1.3): a program in a small subset of the PostScript language.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.5, Table 42, Annex B; the operators mean what the PostScript Language Reference, Third Edition, §8.2 says. The
/// inputs are the initial operand stack (the first input deepest) and the values left on it are the outputs. The stack holds 100
/// entries.
/// </para>
/// <para>
/// The program is compiled once to a flat instruction list (conditionals become jumps) and run over a stack on the call stack, so
/// evaluation allocates nothing. Arithmetic is in double precision (PostScript interpreters use single precision; results differ
/// only in the last bits, which <c>eq</c> and <c>ne</c> on computed values can see). Errors the language would raise are repaired
/// and noted once: division by zero and other undefined results give 0, an operand of the wrong type is converted, the wrong number
/// of results is padded with 0 or cut to the topmost; stack overflow or underflow makes every output 0.
/// </para>
/// </remarks>
public sealed class PdfPostScriptFunction : PdfFunction
{
    internal PdfPostScriptFunction(FunctionCache cache, CosObject cosObject, CosReference? reference, CompiledFunction compiled, int expectedInputs, int expectedOutputs)
        : base(cache, cosObject, reference, compiled, expectedInputs, expectedOutputs)
    {
    }

    /// <inheritdoc/>
    public override PdfFunctionType FunctionType => PdfFunctionType.PostScriptCalculator;

    /// <summary>Gets the stream holding the program; <see langword="null"/> when the function object is a dictionary (the function is then invalid).</summary>
    public CosStream? Stream => CosObject as CosStream;
}
