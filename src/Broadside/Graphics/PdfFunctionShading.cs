using Broadside.Graphics.Shadings;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A function-based shading (Type 1): the colour of every point of a domain is a function of its coordinates.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.2, Table 78. The domain is a rectangle mapped into the target space by <see cref="Matrix"/>; points of the
/// target space outside the mapped domain are not painted (or take the <see cref="PdfShading.Background"/> in a shading pattern).
/// Colours come from <see cref="PdfShading.EvaluateFunction"/> with the input (x, y) in domain space.
/// </remarks>
public sealed class PdfFunctionShading : PdfShading
{
    internal PdfFunctionShading(ShadingReader reader)
        : base(reader, PdfShadingType.FunctionBased)
    {
        Domain = Array.AsReadOnly(reader.Numbers(ShadingNames.Domain, 4, [0, 1, 0, 1], DiagnosticCodes.ShadingEntryInvalid));
        Matrix = reader.Matrix(ShadingNames.Matrix, DiagnosticCodes.ShadingEntryInvalid);
        IsValid = reader.IsValid;
    }

    /// <summary>Gets the domain, [x<sub>min</sub> x<sub>max</sub> y<sub>min</sub> y<sub>max</sub>]. Default [0 1 0 1].</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.2, Table 78 (<c>Domain</c>).</remarks>
    public IReadOnlyList<double> Domain { get; }

    /// <summary>Gets the matrix that maps the domain into the shading's target space. Default the identity.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.2, Table 78 (<c>Matrix</c>).</remarks>
    public Matrix Matrix { get; }
}
