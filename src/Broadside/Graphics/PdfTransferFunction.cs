using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// A transfer function as a graphics state holds it: the identity, one function for every colour component, or one function for
/// each of the four components of a CMYK-like device.
/// </summary>
/// <remarks>
/// ISO 32000-2 §10.5 and Table 57 (<c>TR</c>, <c>TR2</c>; deprecated in PDF 2.0). Each function takes one input and gives one
/// output, both in 0 to 1. The device default (<c>TR2 /Default</c>) is a graphics state without a transfer function.
/// </remarks>
public sealed class PdfTransferFunction
{
    internal PdfTransferFunction(CosObject value, IReadOnlyList<PdfFunction> functions)
    {
        Value = value;
        Functions = functions;
    }

    /// <summary>Gets the entry's value as written: the name <c>Identity</c>, a function, or an array of four functions.</summary>
    public CosObject Value { get; }

    /// <summary>Gets a value indicating whether this is the identity (<c>/Identity</c>).</summary>
    public bool IsIdentity => Functions.Count == 0;

    /// <summary>Gets the functions: none for the identity, one applied to every component, or four (cyan or red, magenta or green, yellow or blue, black or gray).</summary>
    public IReadOnlyList<PdfFunction> Functions { get; }
}
